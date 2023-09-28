using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using CeresGLFW;
using ImGuiNET;

namespace CeresGpuDearImgui
{
    public sealed class ImGuiBackend : IDisposable
    {
        private ImGuiIOPtr _io;
        private readonly GLFWWindow _window;
        private double _time;
        private readonly bool[] _mouseJustPressed = new bool[(int)ImGuiMouseCursor.COUNT];
        private readonly IGLFWCursor[] _mouseCursors = new IGLFWCursor[(int)ImGuiMouseCursor.COUNT];
        
        private unsafe delegate void* GetClipboardDelegate(IntPtr userData);
        private unsafe delegate void SetClipboardDelegate(IntPtr useradata, void* str);

        // Need to hold references to these delegates so that they aren't garbaged collected while being used by ImGui
        // ReSharper disable PrivateFieldCanBeConvertedToLocalVariable
        private readonly GetClipboardDelegate _getClipboardDelegate;
        private readonly SetClipboardDelegate _setClipboardDelegate;
        // ReSharper restore PrivateFieldCanBeConvertedToLocalVariable

        public ImGuiBackend(GLFWWindow window, bool installCallbacks, ImGuiIOPtr io)
        {
            _io = io;
            _window = window;
            
            io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;         // We can honor GetMouseCursor() values (optional)
            io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;          // We can honor io.WantSetMousePos requests (optional, rarely used)

            unsafe {
                _getClipboardDelegate = GetClipboardText;
                _setClipboardDelegate = SetClipboardText;
            }
            io.GetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(_getClipboardDelegate);
            io.SetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(_setClipboardDelegate);
            // io.ClipboardUserData = GCHandle.ToIntPtr(_handle);
            
// #if defined(_WIN32)
//     io.ImeWindowHandle = (void*)glfwGetWin32Window(bd->Window);
// #endif

            // Create mouse cursors
            // (By design, on X11 cursors are user configurable and some cursors may be missing. When a cursor doesn't exist,
            // GLFW will emit an error which will often be printed by the app, so we temporarily disable error reporting.
            // Missing cursors will return NULL and our _UpdateMouseCursor() function will use the Arrow cursor instead.)
            // GLFWerrorfun prev_error_callback = glfwSetErrorCallback(NULL);
            _mouseCursors[(int)ImGuiMouseCursor.Arrow] = new GLFWStandardCursor(StandardCursorShape.Arrow);
            _mouseCursors[(int)ImGuiMouseCursor.TextInput] = new GLFWStandardCursor(StandardCursorShape.IBeam);
            _mouseCursors[(int)ImGuiMouseCursor.ResizeNS] = new GLFWStandardCursor(StandardCursorShape.VResize);
            _mouseCursors[(int)ImGuiMouseCursor.ResizeEW] = new GLFWStandardCursor(StandardCursorShape.HResize);
            _mouseCursors[(int)ImGuiMouseCursor.Hand] = new GLFWStandardCursor(StandardCursorShape.Hand);
#if GLFW_HAS_NEW_CURSORS
    bd->MouseCursors[ImGuiMouseCursor_ResizeAll] = glfwCreateStandardCursor(GLFW_RESIZE_ALL_CURSOR);
    bd->MouseCursors[ImGuiMouseCursor_ResizeNESW] = glfwCreateStandardCursor(GLFW_RESIZE_NESW_CURSOR);
    bd->MouseCursors[ImGuiMouseCursor_ResizeNWSE] = glfwCreateStandardCursor(GLFW_RESIZE_NWSE_CURSOR);
    bd->MouseCursors[ImGuiMouseCursor_NotAllowed] = glfwCreateStandardCursor(GLFW_NOT_ALLOWED_CURSOR);
#else
            _mouseCursors[(int)ImGuiMouseCursor.ResizeAll] = new GLFWStandardCursor(StandardCursorShape.Arrow);
            _mouseCursors[(int)ImGuiMouseCursor.ResizeNESW] = new GLFWStandardCursor(StandardCursorShape.Arrow);
            _mouseCursors[(int)ImGuiMouseCursor.ResizeNWSE] = new GLFWStandardCursor(StandardCursorShape.Arrow);
            _mouseCursors[(int)ImGuiMouseCursor.NotAllowed] = new GLFWStandardCursor(StandardCursorShape.Arrow);
#endif
            //glfwSetErrorCallback(prev_error_callback);

            // Chain GLFW callbacks: our callbacks will call the user's previously installed callbacks, if any.
            if (installCallbacks)
            {
                //bd->PrevUserCallbackWindowFocus = glfwSetWindowFocusCallback(window, ImGui_ImplGlfw_WindowFocusCallback);
                //bd->PrevUserCallbackCursorEnter = glfwSetCursorEnterCallback(window, ImGui_ImplGlfw_CursorEnterCallback);
                window.MouseButtonChanged += MouseButtonCallback;
                window.ScrollChanged += ScrollCallback;
                window.KeyChanged += KeyCallback;
                window.CharacterInput += CharCallback;
                //bd->PrevUserCallbackMonitor = glfwSetMonitorCallback(ImGui_ImplGlfw_MonitorCallback);
            }
        }
        
        private void ReleaseUnmanagedResources()
        {
            _io.GetClipboardTextFn = IntPtr.Zero;
            _io.SetClipboardTextFn = IntPtr.Zero;
        }

        public void Dispose()
        {
            _window.MouseButtonChanged -= MouseButtonCallback;
            _window.ScrollChanged -= ScrollCallback;
            _window.KeyChanged -= KeyCallback;
            _window.CharacterInput -= CharCallback;

            foreach (IGLFWCursor cursor in _mouseCursors) {
                cursor.Dispose();
            }
            
            // io.BackendPlatformName = NULL;
            // io.BackendPlatformUserData = NULL;
            // IM_DELETE(bd);
            
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~ImGuiBackend() {
            ReleaseUnmanagedResources();
        }
        
        // void ImGui_ImplGlfw_WindowFocusCallback(GLFWwindow* window, int focused)
        // {
        //     ImGui_ImplGlfw_Data* bd = ImGui_ImplGlfw_GetBackendData();
        //     if (bd->PrevUserCallbackWindowFocus != NULL && window == bd->Window)
        //         bd->PrevUserCallbackWindowFocus(window, focused);
        //
        //     ImGuiIO& io = ImGui::GetIO();
        //     io.AddFocusEvent(focused != 0);
        // }
        
        private void MouseButtonCallback(int button, InputAction action, Mod mods)
        {
            if (action == InputAction.Press && button >= 0 && button < _mouseJustPressed.Length) {
                _mouseJustPressed[button] = true;
            }
        }
        
        private void ScrollCallback(double xoffset, double yoffset)
        {
            _io.MouseWheelH += (float)xoffset;
            _io.MouseWheel += (float)yoffset;
        }

        private void KeyCallback(Key key, int scancode, InputAction action, Mod mods)
        {
            if (action != InputAction.Press && action != InputAction.Release) {
                return;
            }

            UpdateKeyModifiers(mods);

            Key keycode = TranslateUntranslatedKey(key, scancode);

            ImGuiIOPtr io = ImGui.GetIO();
            ImGuiKey imgui_key = KeyToImGuiKey(keycode);
            io.AddKeyEvent(imgui_key, action == InputAction.Press);
            io.SetKeyEventNativeData(imgui_key, (int)keycode, scancode); // To support legacy indexing (<1.87 user code)
        }

        private void UpdateKeyModifiers(Mod mods)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.AddKeyEvent(ImGuiKey.ModCtrl, (mods & Mod.CONTROL) != 0);
            io.AddKeyEvent(ImGuiKey.ModShift, (mods & Mod.SHIFT) != 0);
            io.AddKeyEvent(ImGuiKey.ModAlt, (mods & Mod.ALT) != 0);
            io.AddKeyEvent(ImGuiKey.ModSuper, (mods & Mod.SUPER) != 0);
        }
        
        private static readonly Key[] char_keys = { Key.GRAVE_ACCENT, Key.MINUS, Key.EQUAL, Key.LEFT_BRACKET, Key.RIGHT_BRACKET, Key.BACKSLASH, Key.COMMA, Key.SEMICOLON, Key.APOSTROPHE, Key.PERIOD, Key.SLASH };

        static Key TranslateUntranslatedKey(Key key, int scancode)
        {
            // GLFW 3.1+ attempts to "untranslate" keys, which goes the opposite of what every other framework does, making using lettered shortcuts difficult.
            // (It had reasons to do so: namely GLFW is/was more likely to be used for WASD-type game controls rather than lettered shortcuts, but IHMO the 3.1 change could have been done differently)
            // See https://github.com/glfw/glfw/issues/1502 for details.
            // Adding a workaround to undo this (so our keys are translated->untranslated->translated, likely a lossy process).
            // This won't cover edge cases but this is at least going to cover common cases.
            if (key >= Key.KP_0 && key <= Key.KP_EQUAL) {
                return key;
            }
            
            string? key_name = GLFW.GetKeyName(key, scancode);
            if (key_name != null && key_name.Length == 1 && key_name[0] != 0)
            {
                const string char_names = "`-=[]\\,;\'./";
                Debug.Assert(char_keys.Length == char_names.Length);
                if (key_name[0] >= '0' && key_name[0] <= '9')               { key = Key.NUM_0 + (key_name[0] - '0'); }
                else if (key_name[0] >= 'A' && key_name[0] <= 'Z')          { key = Key.A + (key_name[0] - 'A'); } 
                else {
                    int index = char_names.IndexOf(key_name[0]);
                    if (index > 0) {
                        key = char_keys[index];
                    }
                }
            }
            // if (action == GLFW_PRESS) printf("key %d scancode %d name '%s'\n", key, scancode, key_name);

            return key;
        }
        
        private void CharCallback(uint c)
        {
            _io.AddInputCharacter(c);
        }

        private void UpdateMousePosAndButtons()
        {
            ImGuiIOPtr io = ImGui.GetIO();
            
            Vector2 mouse_pos_prev = io.MousePos;
            io.MousePos = new Vector2(-float.MaxValue, -float.MaxValue);

            // GLFW's behaviour between macOS and everything else if broken.
            int windowSizeX, windowSizeY;
            _window.GetSize(out windowSizeX, out windowSizeY);
            
            int framebufferWidth, framebufferHeight;
            _window.GetFramebufferSize(out framebufferWidth, out framebufferHeight);

            // Based on GLFW's behaviour, this will always be 1 on windows-like platforms, and content scale on macOS-like platforms.
            // WHY CANT GLFW JUST MAKE THIS CONSISTENT!?!?
            Vector2 framebufferCoordinateScale =
                new Vector2(framebufferWidth / (float)windowSizeX, framebufferHeight / (float)windowSizeY);
            
            Vector2 contentScale;
            _window.GetContentScale(out contentScale.X, out contentScale.Y);

            // This will always be 1 on macOS-like platforms, and contentScale on windows-like platforms. 
            Vector2 finalScale = contentScale / framebufferCoordinateScale;
            
            // Update mouse buttons
            // (if a mouse press event came, always pass it as "mouse held this frame", so we don't miss click-release events that are shorter than 1 frame)
            
            for (int i = 0; i < io.MouseDown.Count; i++) {
                io.MouseDown[i] = _mouseJustPressed[i] || _window.GetMouseButton(i) != InputAction.Release;
                _mouseJustPressed[i] = false;
            }
            
            bool focused = _window.GetAttrib(WindowAttribute.Focused) != 0;

            // Set OS mouse position from Dear ImGui if requested (rarely used, only when ImGuiConfigFlags_NavEnableSetMousePos is enabled by user)
            if (io.WantSetMousePos && focused) {
                _window.SetCursorPos(mouse_pos_prev.X * finalScale.X, mouse_pos_prev.Y * finalScale.Y);
            }

            // Set Dear ImGui mouse position from OS position
            //if (mouse_window != null) {
                double mouse_x, mouse_y;
                _window.GetCursorPos(out mouse_x, out mouse_y);

                io.MousePos = new Vector2((float)mouse_x / finalScale.X, (float)mouse_y / finalScale.Y);
            //}
        }

        private void UpdateMouseCursor()
        {
            ImGuiIOPtr io = ImGui.GetIO();
            if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) > 0 || _window.GetCursorMode() == CursorMode.Disabled) {
                return;
            }
            
            ImGuiMouseCursor imgui_cursor = ImGui.GetMouseCursor();
            if (imgui_cursor == ImGuiMouseCursor.None || io.MouseDrawCursor) {
                // Hide OS mouse cursor if imgui is drawing it or if it wants no cursor
                _window.SetCursorMode(CursorMode.Hidden);
            } else {
                // Show OS mouse cursor
                // FIXME-PLATFORM: Unfocused windows seems to fail changing the mouse cursor with GLFW 3.2, but 3.3 works here.
                _window.SetCursor(_mouseCursors[(int)imgui_cursor] != null ? _mouseCursors[(int)imgui_cursor] : _mouseCursors[(int)ImGuiMouseCursor.Arrow]);
                _window.SetCursorMode(CursorMode.Normal);
            }
        }

        private void MAP_BUTTON(ImGuiIOPtr io, bool[] buttons, ImGuiNavInput input, int button)
        {
            if (buttons.Length > button && buttons[button]) {
                io.NavInputs[(int)input] = 1.0f;
            }
        }

        private void MAP_ANALOG(ImGuiIOPtr io, float[] axes, ImGuiNavInput input, int axis, float v0, float v1)
        {
            float v = (axes.Length > axis) ? axes[axis] : v0;
            v = (v - v0) / (v1 - v0);
            if (v > 1.0f) {
                v = 1.0f;
            }
            if (io.NavInputs[(int)input] < v) {
                io.NavInputs[(int)input] = v;
            }
        }
        
        private void UpdateGamepads()
        {
            ImGuiIOPtr io = ImGui.GetIO();
            
            // TODO: Make this faster? Original: memset(io.NavInputs, 0, sizeof(io.NavInputs));
            for (int i = 0, ilen = io.NavInputs.Count; i < ilen; ++i) {
                io.NavInputs[i] = 0;
            }

            if ((io.ConfigFlags & ImGuiConfigFlags.NavEnableGamepad) == 0) {
                return;
            }

            // Update gamepad inputs
            // TODO: Make this faster? Does C# let us inline? Maybe the AOT/JIT already is? Should check.
            // TODO: WE SHOULD JUST USE CALLBACKS?
            // #define MAP_BUTTON(NAV_NO, BUTTON_NO)       { if (buttons_count > BUTTON_NO && buttons[BUTTON_NO] == GLFW_PRESS) io.NavInputs[NAV_NO] = 1.0f; }
            // #define MAP_ANALOG(NAV_NO, AXIS_NO, V0, V1) { float v = (axes_count > AXIS_NO) ? axes[AXIS_NO] : V0; v = (v - V0) / (V1 - V0); if (v > 1.0f) v = 1.0f; if (io.NavInputs[NAV_NO] < v) io.NavInputs[NAV_NO] = v; }
            // TODO: THIS IS VERY EXPENSIVE FOR C#. ALLOCATES AN ARRAY EVERY FRAME!
            // Need to have a callback for when the joystick changes and keep the array around until the joystick has changed.
            float[] axes = GLFW.GetJoystickAxes(0);
            bool[] buttons = GLFW.GetJoystickButtons(0);
            MAP_BUTTON(io, buttons, ImGuiNavInput.Activate,   0);     // Cross / A
            MAP_BUTTON(io, buttons, ImGuiNavInput.Cancel,     1);     // Circle / B
            MAP_BUTTON(io, buttons, ImGuiNavInput.Menu,       2);     // Square / X
            MAP_BUTTON(io, buttons, ImGuiNavInput.Input,      3);     // Triangle / Y
            MAP_BUTTON(io, buttons, ImGuiNavInput.DpadLeft,   13);    // D-Pad Left
            MAP_BUTTON(io, buttons, ImGuiNavInput.DpadRight,  11);    // D-Pad Right
            MAP_BUTTON(io, buttons, ImGuiNavInput.DpadUp,     10);    // D-Pad Up
            MAP_BUTTON(io, buttons, ImGuiNavInput.DpadDown,   12);    // D-Pad Down
            MAP_BUTTON(io, buttons, ImGuiNavInput.FocusPrev,  4);     // L1 / LB
            MAP_BUTTON(io, buttons, ImGuiNavInput.FocusNext,  5);     // R1 / RB
            MAP_BUTTON(io, buttons, ImGuiNavInput.TweakSlow,  4);     // L1 / LB
            MAP_BUTTON(io, buttons, ImGuiNavInput.TweakFast,  5);     // R1 / RB
            MAP_ANALOG(io, axes, ImGuiNavInput.LStickLeft, 0,  -0.3f,  -0.9f);
            MAP_ANALOG(io, axes, ImGuiNavInput.LStickRight,0,  +0.3f,  +0.9f);
            MAP_ANALOG(io, axes, ImGuiNavInput.LStickUp,   1,  +0.3f,  +0.9f);
            MAP_ANALOG(io, axes, ImGuiNavInput.LStickDown, 1,  -0.3f,  -0.9f);
            // #undef MAP_BUTTON
            // #undef MAP_ANALOG
            if (axes.Length > 0 && buttons.Length > 0) {
                io.BackendFlags |= ImGuiBackendFlags.HasGamepad;
            } else {
                io.BackendFlags &= ~ImGuiBackendFlags.HasGamepad;
            }
        }

        public void NewFrame()
        {
            ImGuiIOPtr io = ImGui.GetIO();

            // Setup display size (every frame to accommodate for window resizing)
            //int w, h;
            int framebuffer_w, framebuffer_h;
            //_window.GetSize(out w, out h);
            _window.GetFramebufferSize(out framebuffer_w, out framebuffer_h);
            
            float contentScaleX, contentScaleY;
            _window.GetContentScale(out contentScaleX, out contentScaleY);
            
            io.DisplaySize = new Vector2(framebuffer_w / contentScaleX, framebuffer_h / contentScaleY);
            //if (w > 0 && h > 0) {
                io.DisplayFramebufferScale = new Vector2(contentScaleX, contentScaleY);
            //}

            // Setup time step
            double current_time = GLFW.GetTime();
            io.DeltaTime = _time > 0.0 ? (float)(current_time - _time) : 1.0f / 60.0f;
            _time = current_time;

            UpdateMousePosAndButtons();
            UpdateMouseCursor();

            // Update game controllers (if enabled and available)
            UpdateGamepads();
        }
        
        // TODO: Clipboard Perf: Prevent marshalling of clipboard data to c# string (UTF-16) and then back again

        private unsafe void* GetClipboardText(IntPtr userData)
        {
            return _window.GetClipboardStringRaw();
        }

        private unsafe void SetClipboardText(IntPtr userData, void* str)
        {
            _window.SetClipboardStringRaw(str);
        }
        
        private ImGuiKey KeyToImGuiKey(Key key)
        {
            return key switch {
                Key.TAB => ImGuiKey.Tab
                , Key.LEFT => ImGuiKey.LeftArrow
                , Key.RIGHT => ImGuiKey.RightArrow
                , Key.UP => ImGuiKey.UpArrow
                , Key.DOWN => ImGuiKey.DownArrow
                , Key.PAGE_UP => ImGuiKey.PageUp
                , Key.PAGE_DOWN => ImGuiKey.PageDown
                , Key.HOME => ImGuiKey.Home
                , Key.END => ImGuiKey.End
                , Key.INSERT => ImGuiKey.Insert
                , Key.DELETE => ImGuiKey.Delete
                , Key.BACKSPACE => ImGuiKey.Backspace
                , Key.SPACE => ImGuiKey.Space
                , Key.ENTER => ImGuiKey.Enter
                , Key.ESCAPE => ImGuiKey.Escape
                , Key.APOSTROPHE => ImGuiKey.Apostrophe
                , Key.COMMA => ImGuiKey.Comma
                , Key.MINUS => ImGuiKey.Minus
                , Key.PERIOD => ImGuiKey.Period
                , Key.SLASH => ImGuiKey.Slash
                , Key.SEMICOLON => ImGuiKey.Semicolon
                , Key.EQUAL => ImGuiKey.Equal
                , Key.LEFT_BRACKET => ImGuiKey.LeftBracket
                , Key.BACKSLASH => ImGuiKey.Backslash
                , Key.RIGHT_BRACKET => ImGuiKey.RightBracket
                , Key.GRAVE_ACCENT => ImGuiKey.GraveAccent
                , Key.CAPS_LOCK => ImGuiKey.CapsLock
                , Key.SCROLL_LOCK => ImGuiKey.ScrollLock
                , Key.NUM_LOCK => ImGuiKey.NumLock
                , Key.PRINT_SCREEN => ImGuiKey.PrintScreen
                , Key.PAUSE => ImGuiKey.Pause
                , Key.KP_0 => ImGuiKey.Keypad0
                , Key.KP_1 => ImGuiKey.Keypad1
                , Key.KP_2 => ImGuiKey.Keypad2
                , Key.KP_3 => ImGuiKey.Keypad3
                , Key.KP_4 => ImGuiKey.Keypad4
                , Key.KP_5 => ImGuiKey.Keypad5
                , Key.KP_6 => ImGuiKey.Keypad6
                , Key.KP_7 => ImGuiKey.Keypad7
                , Key.KP_8 => ImGuiKey.Keypad8
                , Key.KP_9 => ImGuiKey.Keypad9
                , Key.KP_DECIMAL => ImGuiKey.KeypadDecimal
                , Key.KP_DIVIDE => ImGuiKey.KeypadDivide
                , Key.KP_MULTIPLY => ImGuiKey.KeypadMultiply
                , Key.KP_SUBTRACT => ImGuiKey.KeypadSubtract
                , Key.KP_ADD => ImGuiKey.KeypadAdd
                , Key.KP_ENTER => ImGuiKey.KeypadEnter
                , Key.KP_EQUAL => ImGuiKey.KeypadEqual
                , Key.LEFT_SHIFT => ImGuiKey.LeftShift
                , Key.LEFT_CONTROL => ImGuiKey.LeftCtrl
                , Key.LEFT_ALT => ImGuiKey.LeftAlt
                , Key.LEFT_SUPER => ImGuiKey.LeftSuper
                , Key.RIGHT_SHIFT => ImGuiKey.RightShift
                , Key.RIGHT_CONTROL => ImGuiKey.RightCtrl
                , Key.RIGHT_ALT => ImGuiKey.RightAlt
                , Key.RIGHT_SUPER => ImGuiKey.RightSuper
                , Key.MENU => ImGuiKey.Menu
                , Key.NUM_0 => ImGuiKey._0
                , Key.NUM_1 => ImGuiKey._1
                , Key.NUM_2 => ImGuiKey._2
                , Key.NUM_3 => ImGuiKey._3
                , Key.NUM_4 => ImGuiKey._4
                , Key.NUM_5 => ImGuiKey._5
                , Key.NUM_6 => ImGuiKey._6
                , Key.NUM_7 => ImGuiKey._7
                , Key.NUM_8 => ImGuiKey._8
                , Key.NUM_9 => ImGuiKey._9
                , Key.A => ImGuiKey.A
                , Key.B => ImGuiKey.B
                , Key.C => ImGuiKey.C
                , Key.D => ImGuiKey.D
                , Key.E => ImGuiKey.E
                , Key.F => ImGuiKey.F
                , Key.G => ImGuiKey.G
                , Key.H => ImGuiKey.H
                , Key.I => ImGuiKey.I
                , Key.J => ImGuiKey.J
                , Key.K => ImGuiKey.K
                , Key.L => ImGuiKey.L
                , Key.M => ImGuiKey.M
                , Key.N => ImGuiKey.N
                , Key.O => ImGuiKey.O
                , Key.P => ImGuiKey.P
                , Key.Q => ImGuiKey.Q
                , Key.R => ImGuiKey.R
                , Key.S => ImGuiKey.S
                , Key.T => ImGuiKey.T
                , Key.U => ImGuiKey.U
                , Key.V => ImGuiKey.V
                , Key.W => ImGuiKey.W
                , Key.X => ImGuiKey.X
                , Key.Y => ImGuiKey.Y
                , Key.Z => ImGuiKey.Z
                , Key.F1 => ImGuiKey.F1
                , Key.F2 => ImGuiKey.F2
                , Key.F3 => ImGuiKey.F3
                , Key.F4 => ImGuiKey.F4
                , Key.F5 => ImGuiKey.F5
                , Key.F6 => ImGuiKey.F6
                , Key.F7 => ImGuiKey.F7
                , Key.F8 => ImGuiKey.F8
                , Key.F9 => ImGuiKey.F9
                , Key.F10 => ImGuiKey.F10
                , Key.F11 => ImGuiKey.F11
                , Key.F12 => ImGuiKey.F12
                , _ => ImGuiKey.None
            };
        }
    }
}