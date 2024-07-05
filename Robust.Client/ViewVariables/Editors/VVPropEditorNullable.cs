using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Robust.Client.ViewVariables.Editors;

internal sealed class VVPropEditorNullable : VVPropEditor
{
    private readonly VVPropEditor _underlyingPropEditor;
    private object? _storedValue;
    private bool _storedReinterpretValues;

    internal VVPropEditorNullable(VVPropEditor underlyingPropEditor)
    {
        _underlyingPropEditor = underlyingPropEditor;

        underlyingPropEditor.OnValueChanged += (val, reinterpretValues) =>
        {
            _storedValue = val;
            _storedReinterpretValues = reinterpretValues;
        };
    }

    protected override Control MakeUI(object? value)
    {
        var hBoxContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
        };

        var box = new CheckBox
        {
            Pressed = value is null,
            Disabled = ReadOnly,
            Text = "Null",
            MinSize = new Vector2(70, 0)
        };
        hBoxContainer.AddChild(box);

        hBoxContainer.AddChild(_underlyingPropEditor.Initialize(value ?? default, ReadOnly));

        if (!ReadOnly)
        {
            // Only send null when pressed. Otherwise, StoredValue.
            box.OnToggled += args =>
                ValueChanged(args.Pressed ? null : _storedValue, !args.Pressed && _storedReinterpretValues);
        }

        return hBoxContainer;
    }
}
