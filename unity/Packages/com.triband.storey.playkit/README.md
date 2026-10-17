# Storey Play Kit

The prototype's play mode as a Unity package: character, follow camera, virtual joystick, lift UI. Reference implementation only; see Plan §2. Also shipped as the runtime package's `Demo Street` sample so it can be imported into a project and edited.

## Measuring on a device

Add **Storey Performance Run** (*Add Component ▸ Storey ▸ Play Kit*) to any object in a scene with the play kit, build for the device and start the app. Once the city has loaded it flies and walks the camera along fixed paths for about two minutes, then shows a report with a button that copies it (also saved as `StoreyPerformance.txt` in the app's persistent data folder). Each path passes when 99 frames in a hundred are under the quality asset's **P99 Frame Ms**. Turn on *Frame Timing Stats* in Player Settings for the CPU and GPU times, and make a *Development Build* for Storey's own stages and the garbage a frame. Storey Diagnostics' **Build Threads** setting measures one thread count against another the same way (docs/PERFORMANCE.md §9.1, §14).
