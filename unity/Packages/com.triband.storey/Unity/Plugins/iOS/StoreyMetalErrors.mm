// Storey Diagnostics on iOS: Metal's own word on the app's GPU work that failed (docs/PERFORMANCE.md §14).
//
// When the GPU faults (a read outside a buffer), or a render pass holds more geometry than its memoryless attachments
// allow, Metal fails that command buffer, and after a fault iOS goes on to ignore the app's GPU work: the picture stops
// changing while the app runs on. A player without Xcode attached reports none of it. This watches the command buffers
// of the app's Metal command queues (Unity's among them) and keeps the first that fail, with what Metal says of each: the
// error's code and description (the GPU's own reason, kIOGPUCommandBufferCallbackError…), the frame it was made in, and
// which passes it held: each encoder's label, debug groups and state (faulted, affected, completed). StoreyDiagnostics.cs
// takes them each frame (StoreyMetalErrors_Take) and notes them.
//
// It replaces the queue class's three command-buffer methods with ones that call the originals and add a completion
// handler (Objective-C's method swizzling): Unity offers no other way to the command buffers it makes. The buffers ask
// Metal for each encoder's state on an error (MTLCommandBufferErrorOptionEncoderExecutionStatus, iOS 14), and are
// otherwise made as before: references retained or not as the caller asked.

#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#import <objc/runtime.h>
#import <os/lock.h>
#include <atomic>
#include <string.h>

namespace
{
    const int kKept = 12;                              // failed command buffers described; the rest are counted
    os_unfair_lock gate = OS_UNFAIR_LOCK_INIT;
    NSMutableArray<NSString*>* pending;                // described, not yet taken by StoreyDiagnostics
    int described;
    std::atomic<long long> completed{0}, failed{0};
    std::atomic<int> frame{0};                         // the main thread's frame, as StoreyDiagnostics last said
    IMP plainImp, unretainedImp, descriptorImp;        // the queue class's own methods
    bool installed;
    // a buffer made inside another hooked call (one of the queue's methods calling another) is watched once, by the outer
    thread_local int depth;

    const char* CodeName(NSInteger code)
    {
        switch (code)   // MTLCommandBufferError
        {
            case 1: return "internal";
            case 2: return "timeout";
            case 3: return "page fault: a read or write outside the GPU's mapped memory";
            case 4: return "access revoked: the app's GPU access was taken away";
            case 7: return "not permitted (in the background)";
            case 8: return "out of memory";
            case 9: return "invalid resource";
            case 10: return "memoryless: more geometry than a pass's memoryless attachments allow";
            case 12: return "stack overflow";
            default: return "unknown";
        }
    }

    const char* StateName(NSInteger state)
    {
        switch (state)   // MTLCommandEncoderErrorState
        {
            case 1: return "completed";
            case 2: return "affected";
            case 3: return "pending";
            case 4: return "FAULTED";
            default: return "unknown";
        }
    }

    // The passes of a failed buffer, when Metal was asked to record their states (iOS 14)
    void AppendEncoders(NSMutableString* s, NSError* error)
    {
        if (@available(iOS 14.0, *))
        {
            id infos = error.userInfo[MTLCommandBufferEncoderInfoErrorKey];
            if (![infos isKindOfClass:[NSArray class]]) return;
            NSArray* list = infos;
            [s appendFormat:@"\n      its %lu encoders:", (unsigned long)list.count];
            int i = 0;
            for (id<MTLCommandBufferEncoderInfo> info in list)
            {
                if (i == 48) { [s appendString:@"\n        …"]; break; }
                [s appendFormat:@"\n        %d %s %@", i++, StateName(info.errorState), info.label.length > 0 ? info.label : @"(no label)"];
                NSArray<NSString*>* signposts = info.debugSignposts;
                if (signposts.count > 0) [s appendFormat:@" [%@]", [signposts componentsJoinedByString:@" > "]];
            }
        }
    }

    void OnCompleted(id<MTLCommandBuffer> cb, int madeIn)
    {
        if (cb.status != MTLCommandBufferStatusError) { completed++; return; }
        long long n = ++failed;
        os_unfair_lock_lock(&gate);
        bool describe = described < kKept;
        if (describe) described++;
        os_unfair_lock_unlock(&gate);
        if (!describe) return;
        @autoreleasepool
        {
            NSError* e = cb.error;
            NSInteger code = e != nil ? e.code : 0;
            NSString* what = e != nil && e.localizedDescription != nil ? e.localizedDescription : @"(no description)";
            NSMutableString* s = [NSMutableString stringWithFormat:@"Metal: a command buffer failed (failure %lld, after %lld completed; made in frame %d): error %ld, %s. %@",
                n, completed.load(), madeIn, (long)code, CodeName(code), what];
            if (cb.label.length > 0) [s appendFormat:@" Its label: %@.", cb.label];
            if (e != nil) AppendEncoders(s, e);
            os_unfair_lock_lock(&gate);
            [pending addObject:s];
            os_unfair_lock_unlock(&gate);
        }
    }

    id Watch(id cb)
    {
        if (cb == nil || depth > 0) return cb;
        int madeIn = frame.load();
        [(id<MTLCommandBuffer>)cb addCompletedHandler:^(id<MTLCommandBuffer> done) { OnCompleted(done, madeIn); }];
        return cb;
    }

    // A buffer made through the queue's descriptor method, asking for the encoders' states on an error (iOS 14), or nil
    id MadeWithStates(id queue, BOOL retained)
    {
        if (descriptorImp == nullptr) return nil;
        if (@available(iOS 14.0, *))
        {
            MTLCommandBufferDescriptor* d = [MTLCommandBufferDescriptor new];
            d.retainedReferences = retained;
            d.errorOptions = MTLCommandBufferErrorOptionEncoderExecutionStatus;
            return ((id (*)(id, SEL, id))descriptorImp)(queue, @selector(commandBufferWithDescriptor:), d);
        }
        return nil;
    }

    // -[MTLCommandQueue commandBuffer]
    id Plain(id self, SEL _cmd)
    {
        depth++;
        id cb = MadeWithStates(self, YES);
        if (cb == nil) cb = ((id (*)(id, SEL))plainImp)(self, _cmd);
        depth--;
        return Watch(cb);
    }

    // -[MTLCommandQueue commandBufferWithUnretainedReferences]
    id Unretained(id self, SEL _cmd)
    {
        depth++;
        id cb = MadeWithStates(self, NO);
        if (cb == nil) cb = ((id (*)(id, SEL))unretainedImp)(self, _cmd);
        depth--;
        return Watch(cb);
    }

    // -[MTLCommandQueue commandBufferWithDescriptor:] (iOS 14)
    id WithDescriptor(id self, SEL _cmd, id descriptor)
    {
        id use = descriptor;
        if (@available(iOS 14.0, *))
        {
            MTLCommandBufferDescriptor* d = descriptor;
            if (d != nil && (d.errorOptions & MTLCommandBufferErrorOptionEncoderExecutionStatus) == 0)
            {
                MTLCommandBufferDescriptor* copy = [d copy];
                copy.errorOptions = (MTLCommandBufferErrorOption)(d.errorOptions | MTLCommandBufferErrorOptionEncoderExecutionStatus);
                use = copy;
            }
        }
        depth++;
        id cb = ((id (*)(id, SEL, id))descriptorImp)(self, _cmd, use);
        depth--;
        return Watch(cb);
    }

    // The class's method replaced by ours, its own kept in *original (left null when the class has no such method)
    void Hook(Class cls, SEL sel, IMP ours, IMP* original)
    {
        Method m = class_getInstanceMethod(cls, sel);
        if (m == nullptr) return;
        *original = method_getImplementation(m);
        class_replaceMethod(cls, sel, ours, method_getTypeEncoding(m));
    }
}

// Start watching; 1 when the command queues' class is hooked. Main thread, once (again is a no-op).
extern "C" int StoreyMetalErrors_Install(void)
{
    if (installed) return 1;
    @autoreleasepool
    {
        // the class of a queue made here is the class of Unity's: one GPU, one driver, one queue class
        id<MTLDevice> device = MTLCreateSystemDefaultDevice();
        id<MTLCommandQueue> queue = device != nil ? [device newCommandQueue] : nil;
        if (queue == nil) return 0;
        Class cls = object_getClass(queue);
        pending = [NSMutableArray array];
        // the descriptor method first: the other two make their buffers through it once it is known
        if (@available(iOS 14.0, *)) Hook(cls, @selector(commandBufferWithDescriptor:), (IMP)WithDescriptor, &descriptorImp);
        Hook(cls, @selector(commandBuffer), (IMP)Plain, &plainImp);
        Hook(cls, @selector(commandBufferWithUnretainedReferences), (IMP)Unretained, &unretainedImp);
        installed = plainImp != nullptr || unretainedImp != nullptr || descriptorImp != nullptr;
    }
    return installed ? 1 : 0;
}

// The frame the main thread is on: a failed buffer names the frame it was made in.
extern "C" void StoreyMetalErrors_Frame(int f) { frame.store(f); }

// The next failed buffer's description into buffer (UTF-8, cut to size - 1 bytes and ended with a 0); its length, 0 for none.
extern "C" int StoreyMetalErrors_Take(char* buffer, int size)
{
    if (!installed || buffer == nullptr || size < 2) return 0;
    @autoreleasepool
    {
        NSString* s = nil;
        os_unfair_lock_lock(&gate);
        if (pending.count > 0) { s = pending[0]; [pending removeObjectAtIndex:0]; }
        os_unfair_lock_unlock(&gate);
        if (s == nil) return 0;
        const char* text = s.UTF8String;
        size_t n = text != nullptr ? strlen(text) : 0;
        if (n > (size_t)size - 1) n = (size_t)size - 1;
        if (n > 0) memcpy(buffer, text, n);
        buffer[n] = 0;
        return (int)n;
    }
}

// Command buffers that completed, and that failed, since the hook went in.
extern "C" long long StoreyMetalErrors_Completed(void) { return completed.load(); }
extern "C" long long StoreyMetalErrors_Failed(void) { return failed.load(); }
