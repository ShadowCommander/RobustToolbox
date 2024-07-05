using System;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Robust.Client.ViewVariables.Editors
{
    public sealed class VVPropEditorTimeSpan : VVPropEditor
    {
        protected override Control MakeUI(object? value)
        {
            if (value is not TimeSpan ts)
                ts = TimeSpan.Zero;
            var lineEdit = new LineEdit
            {
                Text = ts.ToString(),
                Editable = !ReadOnly,
                MinSize = new(240, 0)
            };

            lineEdit.OnTextEntered += e =>
            {
                if (TimeSpan.TryParse(e.Text, out var span))
                    ValueChanged(span);
            };

            return lineEdit;
        }
    }
}
