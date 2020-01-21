using NUnit.Framework;
using Robust.Client.Input;
using Robust.Client.Interfaces.UserInterface;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Robust.UnitTesting.Client.UserInterface.Controls
{
    [TestFixture]
    [TestOf(typeof(LineEdit))]
    class LineEditTest : RobustUnitTest
    {
        public override UnitTestProject Project => UnitTestProject.Client;

        private IUserInterfaceManagerInternal _userInterfaceManager;

        [OneTimeSetUp]
        public void Setup()
        {
            _userInterfaceManager = IoCManager.Resolve<IUserInterfaceManagerInternal>();
            _userInterfaceManager.InitializeTesting();
        }

        [Test]
        public void TestHoverNoSelect()
        {
            var lineEdit = new LineEdit { CustomMinimumSize = (20, 0) };
            var textEventArgs = new TextEventArgs('a');
            
            lineEdit.MouseEntered();
            _userInterfaceManager.TextEntered(textEventArgs);
            Assert.IsEmpty(lineEdit.Text);

            lineEdit.Dispose();
        }

        [Test]
        public void TestHoverSelected()
        {
            var lineEdit = new LineEdit { CustomMinimumSize = (20, 0) };
            var mouseEventArgsDown = new BoundKeyEventArgs(EngineKeyFunctions.Use, BoundKeyState.Down, new ScreenCoordinates(10, 5), true);
            var mouseEventArgsUp = new BoundKeyEventArgs(EngineKeyFunctions.Use, BoundKeyState.Up, new ScreenCoordinates(10, 5), true);
            var textEventArgs = new TextEventArgs('a');

            lineEdit.MouseEntered();
            _userInterfaceManager.KeyBindDown(mouseEventArgsDown);
            _userInterfaceManager.KeyBindDown(mouseEventArgsUp);
            _userInterfaceManager.TextEntered(textEventArgs);
            Assert.IsEmpty(lineEdit.Text);

            lineEdit.Dispose();
        }

        [Test]
        public void TestNoHoverSelected()
        {
            var lineEdit = new LineEdit { CustomMinimumSize = (20, 0) };
            var mouseEventArgsDown = new BoundKeyEventArgs(EngineKeyFunctions.Use, BoundKeyState.Down, new ScreenCoordinates(10, 5), true);
            var mouseEventArgsUp = new BoundKeyEventArgs(EngineKeyFunctions.Use, BoundKeyState.Up, new ScreenCoordinates(10, 5), true);
            var textEventArgs = new TextEventArgs('a');

            _userInterfaceManager.KeyBindDown(mouseEventArgsDown);
            _userInterfaceManager.KeyBindDown(mouseEventArgsUp);
            _userInterfaceManager.TextEntered(textEventArgs);
            Assert.AreEqual("a", lineEdit.Text);

            lineEdit.Dispose();
        }

        [Test]
        public void TestNoHoverDeselect()
        {
            var lineEdit = new LineEdit { CustomMinimumSize = (20, 0) };
            var mouseEventArgsDown = new BoundKeyEventArgs(EngineKeyFunctions.Use, BoundKeyState.Down, new ScreenCoordinates(10, 5), true);
            var mouseEventArgsUp = new BoundKeyEventArgs(EngineKeyFunctions.Use, BoundKeyState.Up, new ScreenCoordinates(10, 5), true);
            var mouseEventArgsDown2 = new BoundKeyEventArgs(EngineKeyFunctions.Use, BoundKeyState.Down, new ScreenCoordinates(0, 30), true);
            var mouseEventArgsUp2 = new BoundKeyEventArgs(EngineKeyFunctions.Use, BoundKeyState.Up, new ScreenCoordinates(0, 30), true);

            _userInterfaceManager.KeyBindDown(mouseEventArgsDown);
            _userInterfaceManager.KeyBindDown(mouseEventArgsUp);
            _userInterfaceManager.KeyBindDown(mouseEventArgsDown2);
            _userInterfaceManager.KeyBindDown(mouseEventArgsUp2);
            Assert.IsFalse(mouseEventArgsDown2.Handled);
            Assert.IsFalse(mouseEventArgsUp2.Handled);

            lineEdit.Dispose();
        }

        [Test]
        public void TestHoverSelect()
        {
            var lineEdit = new LineEdit { CustomMinimumSize = (20, 0) };
            var mouseEventArgsDown = new BoundKeyEventArgs(EngineKeyFunctions.Use, BoundKeyState.Down, new ScreenCoordinates(10, 5), true);
            var mouseEventArgsUp = new BoundKeyEventArgs(EngineKeyFunctions.Use, BoundKeyState.Up, new ScreenCoordinates(10, 5), true);

            lineEdit.MouseEntered();
            _userInterfaceManager.KeyBindDown(mouseEventArgsDown);
            _userInterfaceManager.KeyBindDown(mouseEventArgsUp);
            Assert.IsTrue(mouseEventArgsDown.Handled);
            Assert.IsTrue(mouseEventArgsUp.Handled);

            lineEdit.Dispose();
        }

        [Test]
        public void TestSelectedKeybind()
        {
            var lineEdit = new LineEdit { CustomMinimumSize = (20, 0), Text = "Test" };
            var control = new Control { Size = (20, 20) };
            var mouseEventArgsDown = new BoundKeyEventArgs(EngineKeyFunctions.TextBackspace, BoundKeyState.Down, new ScreenCoordinates(30, 5), false);
            var mouseEventArgsUp = new BoundKeyEventArgs(EngineKeyFunctions.TextBackspace, BoundKeyState.Up, new ScreenCoordinates(30, 5), false);

            control.Position = new Vector2(20, 0);

            lineEdit.GrabKeyboardFocus();
            control.MouseEntered();
            _userInterfaceManager.KeyBindDown(mouseEventArgsDown);
            _userInterfaceManager.KeyBindDown(mouseEventArgsUp);
            Assert.IsTrue(mouseEventArgsDown.Handled);
            Assert.IsTrue(mouseEventArgsUp.Handled);
            Assert.Equals("Tes", lineEdit.Text);

            lineEdit.Dispose();
        }
    }
}
