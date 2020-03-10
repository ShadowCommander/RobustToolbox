using System;
using Robust.Client.Graphics;
using Robust.Client.Graphics.Drawing;
using Robust.Shared.Maths;
using Robust.Shared.ViewVariables;

namespace Robust.Client.UserInterface.Controls
{
    public class TextureButton : ContainerButton
    {
        private Vector2 _scale = (1, 1);
        private Texture _textureNormal;
        public const string StylePropertyTexture = "texture";

        public TextureButton()
        {
            DrawModeChanged();
        }

        [ViewVariables]
        public Texture TextureNormal
        {
            get => _textureNormal;
            set
            {
                _textureNormal = value;
                MinimumSizeChanged();
            }
        }

        public Vector2 Scale
        {
            get => _scale;
            set
            {
                _scale = value;
                MinimumSizeChanged();
            }
        }

        protected internal override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            var texture = TextureNormal;

            if (texture == null)
            {
                TryGetStyleProperty(StylePropertyTexture, out texture);
                if (texture == null)
                {
                    return;
                }
            }

            handle.DrawTextureRectRegion(texture, PixelSizeBox);
        }

        protected override Vector2 CalculateMinimumSize()
        {
            var texture = TextureNormal;

            if (texture == null)
            {
                TryGetStyleProperty(StylePropertyTexture, out texture);
            }

            var min = Scale * (texture?.Size ?? Vector2.Zero);
            foreach (var child in Children)
            {
                min = Vector2.ComponentMax(min, child.CombinedMinimumSize);
            }

            return min + ActualStyleBox.MinimumSize / UIScale;
        }
    }
}
