// Storey's build threads on iOS: the utility quality-of-service class (docs/PERFORMANCE.md §9.1).
//
// iOS schedules a thread by its QoS class. A .NET thread has whatever class IL2CPP gives it at creation, and .NET's
// Thread.Priority does nothing there (IL2CPP's POSIX layer leaves it empty), so the build threads competed with Unity's
// main and render threads (user-interactive). Utility is below both: the scheduler runs the build threads when the
// frame's threads leave a core free, and may put them on the efficiency cores. Only a thread can set its own class:
// StoreyThreads.cs calls this on each build thread before its first build (BuildThreads.Configure), and again when the
// setting changes.

#include <pthread/qos.h>

// the class the calling thread had before Storey first set it (-1: not yet), which "utility off" gives back
static __thread int original = -1;

// The calling thread to utility (1), or back to the class it started with (0). 0 when it took, else the errno (EPERM: a
// thread whose scheduling parameters were set the POSIX way is out of the QoS system for good).
int StoreyThreads_SetUtility(int utility)
{
    if (original < 0) original = (int)qos_class_self();
    qos_class_t c = utility ? QOS_CLASS_UTILITY : (qos_class_t)original;
    if (c == QOS_CLASS_UNSPECIFIED) c = QOS_CLASS_DEFAULT;
    return pthread_set_qos_class_self_np(c, 0);
}

// The calling thread's class now: 0x21 user-interactive, 0x19 user-initiated, 0x15 default, 0x11 utility, 0x09
// background, 0 unspecified.
int StoreyThreads_Class(void) { return (int)qos_class_self(); }

// The class the calling thread started with, -1 before StoreyThreads_SetUtility.
int StoreyThreads_Original(void) { return original; }
