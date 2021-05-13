using System.Collections.Generic;
using System.Threading;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.ViewVariables.Traits;
using Robust.Shared.Input;
using Robust.Shared.Serialization;
using Robust.Shared.ViewVariables;
using Robust.Shared.Utility;
using Timer = Robust.Shared.Timing.Timer;

namespace Robust.Client.ViewVariables.Instances
{
    internal class ViewVariablesInstanceObject : ViewVariablesInstance
    {
        private TabContainer _tabs = default!;
        private Button _refreshButton = default!;
        private int _tabCount;

        private readonly List<ViewVariablesTrait> _traits = new();

        private CancellationTokenSource _refreshCancelToken = new();

        public ViewVariablesRemoteSession? Session { get; private set; }
        public object? Object { get; private set; }

        public ViewVariablesInstanceObject(IViewVariablesManagerInternal vvm, IRobustSerializer robustSerializer)
            : base(vvm, robustSerializer) { }

        public override void Initialize(SS14Window window, object obj)
        {
            Object = obj;
            var type = obj.GetType();

            var title = PrettyPrint.PrintUserFacingWithType(obj, out var subtitle);

            _wrappingInit(window, title, subtitle);
            foreach (var trait in TraitsFor(ViewVariablesManager.TraitIdsFor(type)))
            {
                trait.Initialize(this);
                _traits.Add(trait);
            }
            _refresh();
        }

        public override void Initialize(SS14Window window,
            ViewVariablesBlobMetadata blob, ViewVariablesRemoteSession session)
        {
            Session = session;

            _wrappingInit(window, $"[SERVER] {blob.Stringified}", blob.ObjectTypePretty);
            foreach (var trait in TraitsFor(blob.Traits))
            {
                trait.Initialize(this);
                _traits.Add(trait);
            }
            _refresh();
        }

        private void _wrappingInit(SS14Window window, string top, string bottom)
        {
            // Wrapping containers.
            var scrollContainer = new ScrollContainer();
            //scrollContainer.SetAnchorPreset(Control.LayoutPreset.Wide, true);
            window.Contents.AddChild(scrollContainer);
            var vBoxContainer = new VBoxContainer
            {
                HorizontalExpand = true,
                VerticalExpand = true,
            };
            scrollContainer.AddChild(vBoxContainer);

            // Handle top bar.
            {
                var headBox = new HBoxContainer();
                var name = MakeTopBar(top, bottom);
                name.HorizontalExpand = true;
                headBox.AddChild(name);

                _refreshButton = new Button {Text = "Refresh", ToolTip = "RMB to toggle auto-refresh."};
                _refreshButton.OnPressed += _ => _refresh();
                _refreshButton.OnKeyBindDown += OnButtonKeybindDown;
                headBox.AddChild(_refreshButton);
                vBoxContainer.AddChild(headBox);
            }

            _tabs = new TabContainer();
            vBoxContainer.AddChild(_tabs);
        }

        private void OnButtonKeybindDown(GUIBoundKeyEventArgs eventArgs)
        {
            if (eventArgs.Function == EngineKeyFunctions.UIRightClick)
            {
                _refreshButton.ToggleMode = !_refreshButton.ToggleMode;
                _refreshButton.Pressed = !_refreshButton.Pressed;

                _refreshCancelToken.Cancel();

                if (!_refreshButton.Pressed) return;

                _refreshCancelToken = new CancellationTokenSource();
                Timer.SpawnRepeating(500, _refresh, _refreshCancelToken.Token);

            } else if (eventArgs.Function == EngineKeyFunctions.UIClick)
            {
                _refreshButton.ToggleMode = false;
            }
        }

        public override void Close()
        {
            base.Close();

            _refreshCancelToken.Cancel();

            if (Session != null && !Session.Closed)
            {
                ViewVariablesManager.CloseSession(Session);
            }
        }

        public void AddTab(string title, Control control)
        {
            _tabs.AddChild(control);
            _tabs.SetTabTitle(_tabCount++, title);
        }

        private void _refresh()
        {
            // TODO: I'm fully aware the ToString() isn't updated.
            // Eh.
            foreach (var trait in _traits)
            {
                trait.Refresh();
            }
        }

        private List<ViewVariablesTrait> TraitsFor(ICollection<object> traitData)
        {
            var list = new List<ViewVariablesTrait>(traitData.Count);
            if (traitData.Contains(ViewVariablesTraits.Members))
            {
                list.Add(new ViewVariablesTraitMembers(ViewVariablesManager, _robustSerializer));
            }

            if (traitData.Contains(ViewVariablesTraits.Enumerable))
            {
                list.Add(new ViewVariablesTraitEnumerable());
            }

            return list;
        }
    }
}
