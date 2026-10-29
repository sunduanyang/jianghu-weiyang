# Original touch input boundary

`OriginalInputAdapter.cs` restores the supplied APK's mounted controls rather than inferring input radius from the rendered fighter. `scripts/export_bloody_input_source.py` extracts the current scene references, source component fields, options asset and TabletModeCanvas constants into `Runtime/Resources/OriginalInputSource.json`.

## Mounted source configuration

MainScene PlayerControls GameObject 4700 has PlayerTouchControls 19964. Its NubLeft is handle RectTransform 12301 under **JoystickLeft on the screen's right side**. Its NubRight is handle 12300 under **JoystickRight on the screen's left side**. The EventTrigger callbacks agree: screen right raises OnHandLeftInputPressed/Released; screen left raises OnHandRightInputPressed/Released.

Both DynamicJoystick instances have background size 300×300 (radius 150), handleRange 1, deadZone 0, AxisOptions.Both, snapX/snapY true, and moveThreshold 1. PlayerInputHandler reads the handle's **Transform.localPosition**, not the snapped Horizontal/Vertical/Direction getter; snap flags therefore do not snap the combat nubs.

CanvasScaler 21039 uses ScaleWithScreenSize and ScreenMatchMode.Expand, so its scale is `min(screenWidth/referenceWidth, screenHeight/referenceHeight)`. Its serialized reference is 1920×1080, but this is changed at runtime by TabletModeCanvas 18789 (`dmp`, VA 0x36aefb8):

| Original mode | Reference size | Horizontal padding | Top / bottom padding | Bottom-element Y |
| --- | --- | --- | --- | --- |
| Explicit PC mode | 3000×1080 | -750 each side | 100 / 100 | 250 |
| PC mode off, float aspect < 16/9 | 2500×1080 | -250 each side | 120 / 150 | 0 |
| PC mode off, aspect >= 16/9 | 1920×1080 | 0 | 0 / 0 | 50 |

The threshold and the 3000/2500 constants are read from the original ELF (VA 0x120aff0, 0x120a7c0 and 0x120b2f8). The mounted pcModeOn is false; Awake writes that setting to hq.PCMode. Running the recovered game on Windows does not itself authorize changing this original setting.

The mounted OptionsData asset has VisibleJoysticks true and FixedJoysticks false. DynamicJoystick.dtx (`0x36c671c`) sets canvas-group alpha from VisibleJoysticks and changes moveThreshold to 1000 for FixedJoysticks, or 1 otherwise. The fixed option does not remove the source pointer-down recenter operation.

## Pointer behavior and output

DynamicJoystick.OnPointerDown (`0x36c67bc`) positions its background using Joystick.duo, shows it and calls the base pointer-down handler **twice**. Each base call tail-calls OnDrag (`0x36cbf8c→0x36cbf90`). The source adapter retains both passes.

OnDrag computes each component as `(pointerScreenPosition - backgroundScreenPosition)/(backgroundRadius*canvas.scaleFactor)`. It applies axis selection, then calls DynamicJoystick.dty (`0x36c68d4`). If magnitude exceeds moveThreshold, that method moves background.anchoredPosition by `normalizedInput*(magnitude-threshold)*radius`. Base dty (`0x36cc1e0`) zeroes input inside the dead zone and normalizes it above magnitude 1. Handle.anchoredPosition becomes `input*radius*handleRange`. This changing background center is why a stateless initial-drag ClampMagnitude cannot reproduce a sequence of dragging out and then back.

OnPointerUp hides the background, zeroes the input and zeroes the handle anchored position (`0x36c6888→0x36cc24c`). Canceled pointers use that same release operation at the host boundary.

GetNetworkInput (`0x3656540`) quantizes the local nub components at 1/1024, rounding halves away from zero before zig-zag packing. FixedUpdateNetwork decodes them, rejects nonfinite vectors and clamps magnitude to the source limit 155; the original joystick itself reaches 150. `GetNub(screenLeft)` exposes the UI handle position, while `GetNetworkNub(screenLeft)` exposes this decoded network value.

`ReadOriginalFields` maps screen right to native NubLeft/HandDownLeft, and screen left to NubRight/HandDownRight. `ApplyToRig` sends those native fields through the rig's existing cross-active-flag method. The rig method's earlier screenLeft/screenRight parameter names do not describe the source scene binding.

Screen-left presses produce MoveTapRight/button 1; screen-right presses produce MoveTapLeft/button 0. PlayerInputHandler.FixedUpdateNetwork dispatches button 1 to OnGoLeft and button 0 to OnGoRight when the mover is not flipped; flipping exchanges them. Thus the host's physical-screen helper uses `OnOriginalMoveTap(flipped ? !screenLeft : screenLeft)`.

## Host integration

Keep one persistent adapter and retain the host's current pointer capture and PC button-to-pointer policy:

1. Bind the imported HUD with `input.Bind(hud.transform)`. This resolves the exact source RectTransforms, uses their actual Canvas camera projection, and applies the source runtime canvas/layout configuration.
2. For a newly captured pointer, call `input.PointerDown(id, position, screenLeft)`; on success dispatch the existing screen move-tap helper once.
3. Forward captured drag positions through `PointerDrag`, and releases/cancels through `PointerUp`. Reset all pointers with `Reset` when the host resets capture.
4. Call `input.UpdateViewport()` for the original TabletModeCanvas resize behavior and `input.ApplyToRig(player)` when reading input.

The adapter does not register an additional Unity EventTrigger or capture the mouse itself; this avoids duplicating the host's capture behavior. `SetOptions` and PCMode are explicit source settings. SetViewport is available for an offscreen host or numeric verification; UseScreenViewport returns to the current game window.

## Verification and limits

`scripts/verify_bloody_input_port.py` executes original ARM64 OnDrag, its virtual DynamicJoystick.dty, base dty and the original fixed-point instruction sequence. Unity icalls supply explicit screen geometry; native input arithmetic and state changes execute intact. The harness compiles the **actual shipped OriginalInputAdapter.cs** with portable Unity geometry shims and the actual JSON DOM.

Five viewport sizes × PC mode on/off × fixed mode on/off provide 20 continuous 35-point trajectories, including returning toward the initial press after the background moves. The check also verifies pointer-down/up zeroing, release/reset, network quantization and both native hand-field mappings. **2,900 compared values passed, with maximum absolute error 0.** Report: `output/bloody-full-port-20261028/ai-check/input/native-input-comparison.txt`.

This verifies the drag/quantization logic. Actual camera projection, UGUI layout rebuilding, event ordering, physical multitouch dispatch and frame-by-frame whole-match behavior still require Unity/device comparison. Bind the source HUD to retain Unity's projection and resize geometry rather than relying on the unbound PC geometry fallback for those cases.
