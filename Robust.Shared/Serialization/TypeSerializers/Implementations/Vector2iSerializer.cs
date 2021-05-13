using System.Globalization;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Manager.Result;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Robust.Shared.Serialization.TypeSerializers.Implementations
{
    [TypeSerializer]
    public class Vector2iSerializer : ITypeSerializer<Vector2i, ValueDataNode>
    {
        public DeserializationResult Read(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies,
            bool skipHook,
            ISerializationContext? context = null)
        {
            string raw = node.Value;
            string[] args = raw.Split(',');

            if (args.Length != 2)
            {
                throw new InvalidMappingException($"Could not parse {nameof(Vector2)}: '{raw}'");
            }

            var x = int.Parse(args[0], CultureInfo.InvariantCulture);
            var y = int.Parse(args[1], CultureInfo.InvariantCulture);
            var vector = new Vector2i(x, y);

            return new DeserializedValue<Vector2i>(vector);
        }

        public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies,
            ISerializationContext? context = null)
        {
            string raw = node.Value;
            string[] args = raw.Split(',');

            if (args.Length != 2)
            {
                return new ErrorNode(node, "Invalid amount of arguments for Vector2i.");
            }

            return int.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out _) &&
                   int.TryParse(args[1], NumberStyles.Any, CultureInfo.InvariantCulture, out _)
                ? new ValidatedValueNode(node)
                : new ErrorNode(node, "Failed parsing values for Vector2i.");
        }

        public DataNode Write(ISerializationManager serializationManager, Vector2i value, bool alwaysWrite = false,
            ISerializationContext? context = null)
        {
            return new ValueDataNode($"{value.X.ToString(CultureInfo.InvariantCulture)}," +
                                     $"{value.Y.ToString(CultureInfo.InvariantCulture)}");
        }

        public Vector2i Copy(ISerializationManager serializationManager, Vector2i source, Vector2i target,
            bool skipHook,
            ISerializationContext? context = null)
        {
            return new(source.X, source.Y);
        }
    }
}
