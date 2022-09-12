using System;
using System.Numerics;
using CeresGLFW;
using ImGuiNET;

namespace Metalancer
{
    public sealed class ImGuiBackend : IDisposable
    {
        //private GCHandle _handle;

        private ImGuiIOPtr _io;
        private readonly GLFWWindow _window;
        private double _time;
        private readonly bool[] _mouseJustPressed = new bool[(int)ImGuiMouseCursor.COUNT];
        private readonly IGLFWCursor[] _mouseCursors = new IGLFWCursor[(int)ImGuiMouseCursor.COUNT];
        private bool _installedCallbacks;
        
        
        // Chain GLFW callbacks: our callbacks will call the user's previously installed callbacks, if any.
        // GLFWwindowfocusfun      PrevUserCallbackWindowFocus;
        // GLFWcursorenterfun      PrevUserCallbackCursorEnter;
        // GLFWmousebuttonfun      PrevUserCallbackMousebutton;
        // GLFWscrollfun           PrevUserCallbackScroll;
        // GLFWkeyfun              PrevUserCallbackKey;
        // GLFWcharfun             PrevUserCallbackChar;
        // GLFWmonitorfun          PrevUserCallbackMonitor;
        
        public ImGuiBackend(GLFWWindow window, bool installCallbacks, Api clientApi, ImGuiIOPtr io)
        {
            //_handle = GCHandle.Alloc(this, GCHandleType.Normal);
            _io = io;
            _window = window;
            _installedCallbacks = installCallbacks;

            //io.UserData = GCHandle.ToIntPtr(_handle);
            io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;         // We can honor GetMouseCursor() values (optional)
            io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;          // We can honor io.WantSetMousePos requests (optional, rarely used)
            
            // Keyboard mapping. Dear ImGui will use those indices to peek into the io.KeysDown[] array.
            io.KeyMap[(int)ImGuiKey.Tab] = (int)Key.TAB;
            io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)Key.LEFT;
            io.KeyMap[(int)ImGuiKey.RightArrow] = (int)Key.RIGHT;
            io.KeyMap[(int)ImGuiKey.UpArrow] = (int)Key.UP;
            io.KeyMap[(int)ImGuiKey.DownArrow] = (int)Key.DOWN;
            io.KeyMap[(int)ImGuiKey.PageUp] = (int)Key.PAGE_UP;
            io.KeyMap[(int)ImGuiKey.PageDown] = (int)Key.PAGE_DOWN;
            io.KeyMap[(int)ImGuiKey.Home] = (int)Key.HOME;
            io.KeyMap[(int)ImGuiKey.End] = (int)Key.END;
            io.KeyMap[(int)ImGuiKey.Insert] = (int)Key.INSERT;
            io.KeyMap[(int)ImGuiKey.Delete] = (int)Key.DELETE;
            io.KeyMap[(int)ImGuiKey.Backspace] = (int)Key.BACKSPACE;
            io.KeyMap[(int)ImGuiKey.Space] = (int)Key.SPACE;
            io.KeyMap[(int)ImGuiKey.Enter] = (int)Key.ENTER;
            io.KeyMap[(int)ImGuiKey.Escape] = (int)Key.ESCAPE;
            io.KeyMap[(int)ImGuiKey.KeyPadEnter] = (int)Key.KP_ENTER;
            io.KeyMap[(int)ImGuiKey.A] = (int)Key.A;
            io.KeyMap[(int)ImGuiKey.C] = (int)Key.C;
            io.KeyMap[(int)ImGuiKey.V] = (int)Key.V;
            io.KeyMap[(int)ImGuiKey.X] = (int)Key.X;
            io.KeyMap[(int)ImGuiKey.Y] = (int)Key.Y;
            io.KeyMap[(int)ImGuiKey.Z] = (int)Key.Z;
            
            
            // io.SetClipboardTextFn = ImGui_ImplGlfw_SetClipboardText;
            // io.GetClipboardTextFn = ImGui_ImplGlfw_GetClipboardText; 
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
            //_handle.Free();
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
        
        private void MouseButtonCallback(int button, InputAction action, int mods)
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
        
        private void KeyCallback(Key key, int scancode, InputAction action, int mods)
        {
            if (key >= 0 && (int)key < _io.KeysDown.Count) {
                if (action == InputAction.Press) {
                    _io.KeysDown[(int)key] = true;
                }
                if (action == InputAction.Release) {
                    _io.KeysDown[(int)key] = false;
                }
            }

            // Modifiers are not reliable across systems
            _io.KeyCtrl = _io.KeysDown[(int)Key.LEFT_CONTROL] || _io.KeysDown[(int)Key.RIGHT_CONTROL];
            _io.KeyShift = _io.KeysDown[(int)Key.LEFT_SHIFT] || _io.KeysDown[(int)Key.RIGHT_SHIFT];
            _io.KeyAlt = _io.KeysDown[(int)Key.LEFT_ALT] || _io.KeysDown[(int)Key.RIGHT_ALT];
//#ifdef _WIN32
            _io.KeySuper = false;
//#else
//            io.KeySuper = io.KeysDown[GLFW_KEY_LEFT_SUPER] || io.KeysDown[GLFW_KEY_RIGHT_SUPER];
//#endif
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
    }
}