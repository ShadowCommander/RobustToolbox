﻿using System;
using System.Collections.Generic;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.ViewVariables;

namespace Robust.Client.UserInterface
{
    internal sealed class UserInterfaceManager : IUserInterfaceManagerInternal
    {
        [Dependency] private readonly IInputManager _inputManager = default!;
        [Dependency] private readonly IClydeInternal _clyde = default!;
        [Dependency] private readonly IClientConsoleHost _consoleHost = default!;
        [Dependency] private readonly IResourceManager _resourceManager = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IStateManager _stateManager = default!;
        [Dependency] private readonly IClientNetManager _netManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IConfigurationManager _configurationManager = default!;

        [ViewVariables] public UITheme ThemeDefaults { get; private set; } = default!;

        [ViewVariables]
        public Stylesheet? Stylesheet
        {
            get => _stylesheet;
            set
            {
                _stylesheet = value;

                foreach (var root in _roots)
                {
                    if (root.Stylesheet != null)
                    {
                        root.StylesheetUpdateRecursive();
                    }
                }
            }
        }

        [ViewVariables] public Control? KeyboardFocused { get; private set; }

        [ViewVariables] public Control? ControlFocused { get; private set; }

        [ViewVariables] public ViewportContainer MainViewport { get; private set; } = default!;
        [ViewVariables] public LayoutContainer StateRoot { get; private set; } = default!;
        [ViewVariables] public PopupContainer ModalRoot { get; private set; } = default!;
        [ViewVariables] public Control? CurrentlyHovered { get; private set; } = default!;
        [ViewVariables] public float DefaultUIScale => _clyde.DefaultWindowScale.X;
        [ViewVariables] public WindowRoot RootControl { get; private set; } = default!;
        [ViewVariables] public LayoutContainer WindowRoot { get; private set; } = default!;
        [ViewVariables] public LayoutContainer PopupRoot { get; private set; } = default!;
        [ViewVariables] public DropDownDebugConsole DebugConsole { get; private set; } = default!;
        [ViewVariables] public IDebugMonitors DebugMonitors => _debugMonitors;
        private DebugMonitors _debugMonitors = default!;

        private readonly List<Control> _modalStack = new();

        private bool _rendering = true;

        private float _tooltipTimer;

        // set to null when not counting down
        private float? _tooltipDelay;
        private Tooltip _tooltip = default!;
        private bool showingTooltip;
        private Control? _suppliedTooltip;
        private const float TooltipDelay = 1;

        private readonly Queue<Control> _styleUpdateQueue = new();
        private readonly Queue<Control> _measureUpdateQueue = new();
        private readonly Queue<Control> _arrangeUpdateQueue = new();
        private Stylesheet? _stylesheet;
        private ICursor? _worldCursor;
        private bool _needUpdateActiveCursor;

        private readonly List<WindowRoot> _roots = new();
        private readonly Dictionary<WindowId, WindowRoot> _windowsToRoot = new();

        public void Initialize()
        {
            _configurationManager.OnValueChanged(CVars.DisplayUIScale, _uiScaleChanged, true);

            ThemeDefaults = new UIThemeDummy();

            _initializeCommon();

            DebugConsole = new DropDownDebugConsole();
            RootControl.AddChild(DebugConsole);

            _debugMonitors = new DebugMonitors(_gameTiming, _playerManager, _eyeManager, _inputManager, _stateManager,
                _clyde, _netManager, _mapManager);
            RootControl.AddChild(_debugMonitors);

            _inputManager.SetInputCommand(EngineKeyFunctions.ShowDebugConsole,
                InputCmdHandler.FromDelegate(session => DebugConsole.Toggle()));

            _inputManager.SetInputCommand(EngineKeyFunctions.ShowDebugMonitors,
                InputCmdHandler.FromDelegate(enabled: session => { DebugMonitors.Visible = true; },
                    disabled: session => { DebugMonitors.Visible = false; }));

            _inputManager.SetInputCommand(EngineKeyFunctions.HideUI,
                InputCmdHandler.FromDelegate(
                    enabled: session => _rendering = false,
                    disabled: session => _rendering = true));

            _inputManager.UIKeyBindStateChanged += OnUIKeyBindStateChanged;

            _uiScaleChanged(_configurationManager.GetCVar(CVars.DisplayUIScale));
        }

        private void _initializeCommon()
        {
            RootControl = CreateWindowRoot(_clyde.MainWindow);

            RootControl.InvalidateMeasure();
            QueueMeasureUpdate(RootControl);

            _clyde.OnWindowResized += WindowSizeChanged;
            _clyde.OnWindowScaleChanged += WindowContentScaleChanged;
            _clyde.DestroyWindow += WindowDestroyed;

            MainViewport = new MainViewportContainer(_eyeManager)
            {
                Name = "MainViewport"
            };
            RootControl.AddChild(MainViewport);

            StateRoot = new LayoutContainer
            {
                Name = "StateRoot",
                MouseFilter = Control.MouseFilterMode.Ignore
            };
            RootControl.AddChild(StateRoot);

            WindowRoot = new LayoutContainer
            {
                Name = "WindowRoot",
                MouseFilter = Control.MouseFilterMode.Ignore
            };
            RootControl.AddChild(WindowRoot);

            PopupRoot = new LayoutContainer
            {
                Name = "PopupRoot",
                MouseFilter = Control.MouseFilterMode.Ignore
            };
            RootControl.AddChild(PopupRoot);

            ModalRoot = new PopupContainer
            {
                Name = "ModalRoot",
                MouseFilter = Control.MouseFilterMode.Ignore,
            };
            RootControl.AddChild(ModalRoot);

            _tooltip = new Tooltip();
            PopupRoot.AddChild(_tooltip);
            _tooltip.Visible = false;
        }

        public void InitializeTesting()
        {
            ThemeDefaults = new UIThemeDummy();

            _initializeCommon();
        }

        public WindowRoot CreateWindowRoot(IClydeWindow window)
        {
            if (_windowsToRoot.ContainsKey(window.Id))
            {
                throw new ArgumentException("Window already has a UI root.");
            }

            var newRoot = new WindowRoot(window)
            {
                MouseFilter = Control.MouseFilterMode.Ignore,
                HorizontalAlignment = Control.HAlignment.Stretch,
                VerticalAlignment = Control.VAlignment.Stretch,
                UIScaleSet = window.ContentScale.X
            };

            _roots.Add(newRoot);
            _windowsToRoot.Add(window.Id, newRoot);

            newRoot.InvalidateMeasure();
            QueueMeasureUpdate(newRoot);

            return newRoot;
        }

        public void DestroyWindowRoot(IClydeWindow window)
        {
            // Destroy window root if this window had one.
            if (!_windowsToRoot.TryGetValue(window.Id, out var root))
                return;

            _windowsToRoot.Remove(window.Id);
            _roots.Remove(root);

            root.RemoveAllChildren();
        }

        private void WindowDestroyed(WindowDestroyedEventArgs args)
        {
            DestroyWindowRoot(args.Window);
        }

        public void FrameUpdate(FrameEventArgs args)
        {
            // Process queued style & layout updates.
            while (_styleUpdateQueue.Count != 0)
            {
                var control = _styleUpdateQueue.Dequeue();

                if (control.Disposed)
                {
                    continue;
                }

                control.DoStyleUpdate();
            }

            while (_measureUpdateQueue.Count != 0)
            {
                var control = _measureUpdateQueue.Dequeue();

                if (control.Disposed)
                {
                    continue;
                }

                RunMeasure(control);
            }

            while (_arrangeUpdateQueue.Count != 0)
            {
                var control = _arrangeUpdateQueue.Dequeue();

                if (control.Disposed)
                {
                    continue;
                }

                RunArrange(control);
            }

            foreach (var root in _roots)
            {
                root.DoFrameUpdate(args);
            }

            // count down tooltip delay if we're not showing one yet and
            // are hovering the mouse over a control without moving it
            if (_tooltipDelay != null && !showingTooltip)
            {
                _tooltipTimer += args.DeltaSeconds;
                if (_tooltipTimer >= _tooltipDelay)
                {
                    _showTooltip();
                }
            }

            if (_needUpdateActiveCursor)
            {
                _needUpdateActiveCursor = false;
                UpdateActiveCursor();
            }
        }

        private void RunMeasure(Control control)
        {
            if (control.IsMeasureValid || !control.IsInsideTree)
                return;

            if (control.Parent != null)
            {
                RunMeasure(control.Parent);
            }

            if (control is WindowRoot root)
            {
                control.Measure(root.Window.RenderTarget.Size / root.UIScale);
            }
            else if (control.PreviousMeasure.HasValue)
            {
                control.Measure(control.PreviousMeasure.Value);
            }
        }

        private void RunArrange(Control control)
        {
            if (control.IsArrangeValid || !control.IsInsideTree)
                return;

            if (control.Parent != null)
            {
                RunArrange(control.Parent);
            }

            if (control is WindowRoot root)
            {
                control.Arrange(UIBox2.FromDimensions(Vector2.Zero, root.Window.RenderTarget.Size / root.UIScale));
            }
            else if (control.PreviousArrange.HasValue)
            {
                control.Arrange(control.PreviousArrange.Value);
            }
        }

        public bool HandleCanFocusDown(ScreenCoordinates pointerPosition)
        {
            var control = MouseGetControl(pointerPosition);
            var pos = pointerPosition.Position;

            // If we have a modal open and the mouse down was outside it, close said modal.
            while (_modalStack.Count != 0)
            {
                var top = _modalStack[^1];
                var offset = pos - top.GlobalPixelPosition;
                if (!top.HasPoint(offset / top.UIScale))
                {
                    if (top.MouseFilter != Control.MouseFilterMode.Stop)
                        RemoveModal(top);
                    else
                    {
                        ControlFocused?.ControlFocusExited();
                        ControlFocused = top;
                        return false; // prevent anything besides the top modal control from receiving input
                    }
                }
                else
                {
                    break;
                }
            }

            ReleaseKeyboardFocus();

            if (control == null)
            {
                return false;
            }

            ControlFocused?.ControlFocusExited();
            ControlFocused = control;

            if (ControlFocused.CanKeyboardFocus && ControlFocused.KeyboardFocusOnClick)
            {
                ControlFocused.GrabKeyboardFocus();
            }

            return true;
        }

        public void HandleCanFocusUp()
        {
            ControlFocused?.ControlFocusExited();
            ControlFocused = null;
        }

        public void KeyBindDown(BoundKeyEventArgs args)
        {
            if (args.Function == EngineKeyFunctions.CloseModals && _modalStack.Count != 0)
            {
                while (_modalStack.Count > 0)
                {
                    var top = _modalStack[^1];
                    RemoveModal(top);
                }

                args.Handle();
                return;
            }

            var control = ControlFocused ?? KeyboardFocused ?? MouseGetControl(args.PointerLocation);

            if (control == null)
            {
                return;
            }

            var guiArgs = new GUIBoundKeyEventArgs(args.Function, args.State, args.PointerLocation, args.CanFocus,
                args.PointerLocation.Position / control.UIScale - control.GlobalPosition,
                args.PointerLocation.Position - control.GlobalPixelPosition);

            _doGuiInput(control, guiArgs, (c, ev) => c.KeyBindDown(ev));

            if (guiArgs.Handled)
            {
                args.Handle();
            }
        }

        public void KeyBindUp(BoundKeyEventArgs args)
        {
            var control = ControlFocused ?? KeyboardFocused ?? MouseGetControl(args.PointerLocation);
            if (control == null)
            {
                return;
            }

            var guiArgs = new GUIBoundKeyEventArgs(args.Function, args.State, args.PointerLocation, args.CanFocus,
                args.PointerLocation.Position / control.UIScale - control.GlobalPosition,
                args.PointerLocation.Position - control.GlobalPixelPosition);

            _doGuiInput(control, guiArgs, (c, ev) => c.KeyBindUp(ev));

            // Always mark this as handled.
            // The only case it should not be is if we do not have a control to click on,
            // in which case we never reach this.
            args.Handle();
        }

        public void MouseMove(MouseMoveEventArgs mouseMoveEventArgs)
        {
            _resetTooltipTimer();
            // Update which control is considered hovered.
            var newHovered = MouseGetControl(mouseMoveEventArgs.Position);
            if (newHovered != CurrentlyHovered)
            {
                _clearTooltip();
                CurrentlyHovered?.MouseExited();
                CurrentlyHovered = newHovered;
                CurrentlyHovered?.MouseEntered();
                if (CurrentlyHovered != null)
                {
                    _tooltipDelay = CurrentlyHovered.TooltipDelay ?? TooltipDelay;
                }
                else
                {
                    _tooltipDelay = null;
                }

                _needUpdateActiveCursor = true;
            }

            var target = ControlFocused ?? newHovered;
            if (target != null)
            {
                var pos = mouseMoveEventArgs.Position.Position;
                var guiArgs = new GUIMouseMoveEventArgs(mouseMoveEventArgs.Relative / target.UIScale,
                    target,
                    pos / target.UIScale, mouseMoveEventArgs.Position,
                    pos / target.UIScale - target.GlobalPosition,
                    pos - target.GlobalPixelPosition);

                _doMouseGuiInput(target, guiArgs, (c, ev) => c.MouseMove(ev));
            }
        }

        private void UpdateActiveCursor()
        {
            // Consider mouse input focus first so that dragging windows don't act up etc.
            var cursorTarget = ControlFocused ?? CurrentlyHovered;

            if (cursorTarget == null)
            {
                _clyde.SetCursor(_worldCursor);
                return;
            }

            if (cursorTarget.CustomCursorShape != null)
            {
                _clyde.SetCursor(cursorTarget.CustomCursorShape);
                return;
            }

            var shape = cursorTarget.DefaultCursorShape switch
            {
                Control.CursorShape.Arrow => StandardCursorShape.Arrow,
                Control.CursorShape.IBeam => StandardCursorShape.IBeam,
                Control.CursorShape.Hand => StandardCursorShape.Hand,
                Control.CursorShape.Crosshair => StandardCursorShape.Crosshair,
                Control.CursorShape.VResize => StandardCursorShape.VResize,
                Control.CursorShape.HResize => StandardCursorShape.HResize,
                _ => StandardCursorShape.Arrow
            };

            _clyde.SetCursor(_clyde.GetStandardCursor(shape));
        }

        public void MouseWheel(MouseWheelEventArgs args)
        {
            var control = MouseGetControl(args.Position);
            if (control == null)
            {
                return;
            }

            args.Handle();

            var pos = args.Position.Position;

            var guiArgs = new GUIMouseWheelEventArgs(args.Delta, control,
                pos / control.UIScale, args.Position,
                pos / control.UIScale - control.GlobalPosition, pos - control.GlobalPixelPosition);

            _doMouseGuiInput(control, guiArgs, (c, ev) => c.MouseWheel(ev), true);
        }

        public void TextEntered(TextEventArgs textEvent)
        {
            if (KeyboardFocused == null)
            {
                return;
            }

            var guiArgs = new GUITextEventArgs(KeyboardFocused, textEvent.CodePoint);
            KeyboardFocused.TextEntered(guiArgs);
        }

        public void Popup(string contents, string title = "Alert!")
        {
            var popup = new SS14Window
            {
                Title = title
            };

            popup.Contents.AddChild(new Label {Text = contents});
            popup.OpenCentered();
        }

        public Control? MouseGetControl(ScreenCoordinates coordinates)
        {
            if (!_windowsToRoot.TryGetValue(coordinates.Window, out var root))
                return null;

            return _mouseFindControlAtPos(root, coordinates.Position);
        }

        public ScreenCoordinates MousePositionScaled => ScreenToUIPosition(_inputManager.MouseScreenPosition);

        public ScreenCoordinates ScreenToUIPosition(ScreenCoordinates coordinates)
        {
            if (!_windowsToRoot.TryGetValue(coordinates.Window, out var root))
                return default;

            return new ScreenCoordinates(coordinates.Position / root.UIScale, coordinates.Window);
        }

        /// <inheritdoc />
        public void GrabKeyboardFocus(Control control)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            if (!control.CanKeyboardFocus)
            {
                throw new ArgumentException("Control cannot get keyboard focus.", nameof(control));
            }

            if (control == KeyboardFocused)
            {
                return;
            }

            ReleaseKeyboardFocus();

            KeyboardFocused = control;

            KeyboardFocused.KeyboardFocusEntered();
        }

        public void ReleaseKeyboardFocus()
        {
            var oldFocused = KeyboardFocused;
            oldFocused?.KeyboardFocusExited();
            KeyboardFocused = null;
        }

        public void ReleaseKeyboardFocus(Control ifControl)
        {
            if (ifControl == null)
            {
                throw new ArgumentNullException(nameof(ifControl));
            }

            if (ifControl == KeyboardFocused)
            {
                ReleaseKeyboardFocus();
            }
        }

        public ICursor? WorldCursor
        {
            get => _worldCursor;
            set
            {
                _worldCursor = value;
                _needUpdateActiveCursor = true;
            }
        }

        public void ControlHidden(Control control)
        {
            // Does the same thing but it could later be changed so..
            ControlRemovedFromTree(control);
        }

        public void ControlRemovedFromTree(Control control)
        {
            ReleaseKeyboardFocus(control);
            RemoveModal(control);
            if (control == CurrentlyHovered)
            {
                control.MouseExited();
                CurrentlyHovered = null;
                _clearTooltip();
            }

            if (control != ControlFocused) return;
            ControlFocused?.ControlFocusExited();
            ControlFocused = null;
        }

        public void PushModal(Control modal)
        {
            _modalStack.Add(modal);
        }

        public void RemoveModal(Control modal)
        {
            if (_modalStack.Remove(modal))
            {
                modal.ModalRemoved();
            }
        }

        public void Render(IRenderHandle renderHandle)
        {
            // Render secondary windows LAST.
            // This makes it so that (hopefully) the GPU will be done rendering secondary windows
            // by the times we try to blit to them at the end of Clyde's render cycle,
            // So that the GL driver doesn't have to block on glWaitSync.

            foreach (var root in _roots)
            {
                if (root.Window != _clyde.MainWindow)
                {
                    renderHandle.RenderInRenderTarget(root.Window.RenderTarget, () => DoRender(root));
                }
            }

            DoRender(_windowsToRoot[_clyde.MainWindow.Id]);

            void DoRender(WindowRoot root)
            {
                _render(renderHandle, root, Vector2i.Zero, Color.White, null);
            }
        }

        public void QueueStyleUpdate(Control control)
        {
            _styleUpdateQueue.Enqueue(control);
        }

        public void QueueMeasureUpdate(Control control)
        {
            _measureUpdateQueue.Enqueue(control);
            _arrangeUpdateQueue.Enqueue(control);
        }

        public void QueueArrangeUpdate(Control control)
        {
            _arrangeUpdateQueue.Enqueue(control);
        }

        public void CursorChanged(Control control)
        {
            if (control == ControlFocused || control == CurrentlyHovered)
            {
                _needUpdateActiveCursor = true;
            }
        }

        private void _render(IRenderHandle renderHandle, Control control, Vector2i position, Color modulate,
            UIBox2i? scissorBox)
        {
            if (!control.Visible)
            {
                return;
            }

            // Manual clip test with scissor region as optimization.
            var controlBox = UIBox2i.FromDimensions(position, control.PixelSize);

            if (scissorBox != null)
            {
                var clipMargin = control.RectDrawClipMargin;
                var clipTestBox = new UIBox2i(controlBox.Left - clipMargin, controlBox.Top - clipMargin,
                    controlBox.Right + clipMargin, controlBox.Bottom + clipMargin);

                if (!scissorBox.Value.Intersects(clipTestBox))
                {
                    return;
                }
            }

            var handle = renderHandle.DrawingHandleScreen;
            handle.SetTransform(position, Angle.Zero, Vector2.One);
            modulate *= control.Modulate;
            handle.Modulate = modulate * control.ActualModulateSelf;
            var clip = control.RectClipContent;
            var scissorRegion = scissorBox;
            if (clip)
            {
                scissorRegion = controlBox;
                if (scissorBox != null)
                {
                    // Make the final scissor region a sub region of scissorBox
                    var s = scissorBox.Value;
                    var result = s.Intersection(scissorRegion.Value);
                    if (result == null)
                    {
                        // Uhm... No intersection so... don't draw anything at all?
                        return;
                    }

                    scissorRegion = result.Value;
                }

                renderHandle.SetScissor(scissorRegion);
            }

            if (_rendering || control.AlwaysRender)
            {
                control.DrawInternal(renderHandle);
                handle.UseShader(null);
            }

            foreach (var child in control.Children)
            {
                _render(renderHandle, child, position + child.PixelPosition, modulate, scissorRegion);
            }

            if (clip)
            {
                renderHandle.SetScissor(scissorBox);
            }
        }

        private Control? _mouseFindControlAtPos(Control control, Vector2 position)
        {
            for (var i = control.ChildCount - 1; i >= 0; i--)
            {
                var child = control.GetChild(i);
                if (!child.Visible || child.RectClipContent && !child.PixelRect.Contains((Vector2i) position))
                {
                    continue;
                }

                var maybeFoundOnChild = _mouseFindControlAtPos(child, position - child.PixelPosition);
                if (maybeFoundOnChild != null)
                {
                    return maybeFoundOnChild;
                }
            }

            if (control.MouseFilter != Control.MouseFilterMode.Ignore && control.HasPoint(position / control.UIScale))
            {
                return control;
            }

            return null;
        }

        private static void _doMouseGuiInput<T>(Control? control, T guiEvent, Action<Control, T> action,
            bool ignoreStop = false)
            where T : GUIMouseEventArgs
        {
            while (control != null)
            {
                guiEvent.SourceControl = control;
                if (control.MouseFilter != Control.MouseFilterMode.Ignore)
                {
                    action(control, guiEvent);

                    if (guiEvent.Handled || (!ignoreStop && control.MouseFilter == Control.MouseFilterMode.Stop))
                    {
                        break;
                    }
                }

                guiEvent.RelativePosition += control.Position;
                guiEvent.RelativePixelPosition += control.PixelPosition;
                control = control.Parent;
            }
        }

        private static void _doGuiInput<T>(Control? control, T guiEvent, Action<Control, T> action,
            bool ignoreStop = false)
            where T : GUIBoundKeyEventArgs
        {
            while (control != null)
            {
                if (control.MouseFilter != Control.MouseFilterMode.Ignore)
                {
                    action(control, guiEvent);

                    if (guiEvent.Handled || (!ignoreStop && control.MouseFilter == Control.MouseFilterMode.Stop))
                    {
                        break;
                    }
                }

                guiEvent.RelativePosition += control.Position;
                guiEvent.RelativePixelPosition += control.PixelPosition;
                control = control.Parent;
            }
        }

        private void _clearTooltip()
        {
            if (!showingTooltip) return;
            _tooltip.Visible = false;
            if (_suppliedTooltip != null)
            {
                PopupRoot.RemoveChild(_suppliedTooltip);
                _suppliedTooltip = null;
            }

            CurrentlyHovered?.PerformHideTooltip();
            _resetTooltipTimer();
            showingTooltip = false;
        }


        public void HideTooltipFor(Control control)
        {
            if (CurrentlyHovered == control)
            {
                _clearTooltip();
            }
        }

        public Control? GetSuppliedTooltipFor(Control control)
        {
            return CurrentlyHovered == control ? _suppliedTooltip : null;
        }

        private void _resetTooltipTimer()
        {
            _tooltipTimer = 0;
        }

        private void _showTooltip()
        {
            if (showingTooltip) return;
            showingTooltip = true;
            var hovered = CurrentlyHovered;
            if (hovered == null)
            {
                return;
            }

            // show supplied tooltip if there is one
            if (hovered.TooltipSupplier != null)
            {
                _suppliedTooltip = hovered.TooltipSupplier.Invoke(hovered);
                if (_suppliedTooltip != null)
                {
                    PopupRoot.AddChild(_suppliedTooltip);
                    Tooltips.PositionTooltip(_suppliedTooltip);
                }
            }
            else if (!String.IsNullOrWhiteSpace(hovered.ToolTip))
            {
                // show simple tooltip if there is one
                _tooltip.Visible = true;
                _tooltip.Text = hovered.ToolTip;
                Tooltips.PositionTooltip(_tooltip);
            }

            hovered.PerformShowTooltip();
        }

        private void _uiScaleChanged(float newValue)
        {
            foreach (var root in _roots)
            {
                UpdateUIScale(root);
            }
        }

        private void WindowContentScaleChanged(WindowContentScaleEventArgs args)
        {
            if (_windowsToRoot.TryGetValue(args.Window.Id, out var root))
                UpdateUIScale(root);
        }

        private void UpdateUIScale(WindowRoot root)
        {
            var newVal = _configurationManager.GetCVar(CVars.DisplayUIScale);
            root.UIScaleSet = newVal == 0f ? root.Window.ContentScale.X : newVal;

            _propagateUIScaleChanged(root);
            root.InvalidateMeasure();
        }

        private static void _propagateUIScaleChanged(Control control)
        {
            control.UIScaleChanged();

            foreach (var child in control.Children)
            {
                _propagateUIScaleChanged(child);
            }
        }

        private void WindowSizeChanged(WindowResizedEventArgs windowResizedEventArgs)
        {
            if (!_windowsToRoot.TryGetValue(windowResizedEventArgs.Window.Id, out var root))
                return;

            root.InvalidateMeasure();
        }

        /// <summary>
        ///     Converts
        /// </summary>
        /// <param name="args">Event data values for a bound key state change.</param>
        private bool OnUIKeyBindStateChanged(BoundKeyEventArgs args)
        {
            if (args.State == BoundKeyState.Down)
            {
                KeyBindDown(args);
            }
            else
            {
                KeyBindUp(args);
            }

            // If we are in a focused control or doing a CanFocus, return true
            // So that InputManager doesn't propagate events to simulation.
            if (!args.CanFocus && KeyboardFocused != null)
            {
                return true;
            }

            return false;
        }
    }
}
