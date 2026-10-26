using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;

namespace OpenNestCoop.GameSync;

// Interoperability data only: the caller supplies every expected CLR type.
// Packets cannot select a type, constructor, method, assembly, or executable.
internal sealed class ManagedStateCodec
{
    internal sealed class Node
    {
        public string Value;
        public List<Node> Items;
        public Dictionary<string, Node> Fields;
    }
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, MaxDepth = 64 };
    private readonly Func<Type, bool> _allowModel;
    private int _nodes;
    internal ManagedStateCodec(Func<Type, bool> allowModel) { _allowModel = allowModel; }
    internal string Encode(object value, Type expected)
    {
        _nodes = 0;
        var path = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return JsonSerializer.Serialize(Write(value, expected, 0, path), Json);
    }
    internal object Decode(string payload, Type expected)
    {
        if (payload == null || payload.Length > 2000000) throw new InvalidOperationException("Managed state too large");
        _nodes = 0;
        return Read(JsonSerializer.Deserialize<Node>(payload, Json), expected, 0);
    }
    private void Bound(int depth)
    {
        if (depth > 24 || ++_nodes > 40000) throw new InvalidOperationException("Managed state graph exceeds limits");
    }
    private static bool Scalar(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string)
        || type == typeof(Guid) || type == typeof(decimal);
    private static FieldInfo[] Fields(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(f => !f.IsStatic).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
    private static bool Sequence(Type type) => type.IsArray || (type.IsGenericType &&
        (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(HashSet<>)));
    private Node Write(object value, Type type, int depth, HashSet<object> path)
    {
        Bound(depth);
        if (value == null) return null;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (Scalar(type))
        {
            string text = value is Guid guid ? guid.ToString("D") : Convert.ToString(value, CultureInfo.InvariantCulture);
            if (text?.Length > 200000) throw new InvalidOperationException("Managed string too large");
            return new Node { Value = text };
        }
        if (!type.IsValueType && !path.Add(value)) throw new InvalidOperationException("Cyclic managed state");
        try
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var types = type.GetGenericArguments(); var items = new List<Node>();
                foreach (DictionaryEntry entry in (IDictionary)value)
                {
                    items.Add(Write(entry.Key, types[0], depth + 1, path));
                    items.Add(Write(entry.Value, types[1], depth + 1, path));
                }
                return new Node { Items = items };
            }
            if (Sequence(type))
            {
                Type element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                var items = new List<Node>();
                foreach (var item in (IEnumerable)value) items.Add(Write(item, element, depth + 1, path));
                return new Node { Items = items };
            }
            if (!_allowModel(type)) throw new InvalidOperationException("Unsupported managed model: " + type.FullName);
            var fields = new Dictionary<string, Node>(StringComparer.Ordinal);
            foreach (var field in Fields(type)) fields[field.Name] = Write(field.GetValue(value), field.FieldType, depth + 1, path);
            return new Node { Fields = fields };
        }
        finally { if (!type.IsValueType) path.Remove(value); }
    }
    private object Read(Node node, Type type, int depth)
    {
        Bound(depth);
        if (node == null)
        {
            if (type.IsValueType && Nullable.GetUnderlyingType(type) == null) throw new InvalidOperationException("Null value type");
            return null;
        }
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (Scalar(type))
        {
            if (node.Value == null || node.Value.Length > 200000) throw new InvalidOperationException("Invalid scalar");
            if (type == typeof(string)) return node.Value;
            if (type == typeof(Guid)) return Guid.Parse(node.Value);
            if (type.IsEnum) return Enum.Parse(type, node.Value, false);
            return Convert.ChangeType(node.Value, type, CultureInfo.InvariantCulture);
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var items = node.Items ?? throw new InvalidOperationException("Missing dictionary items");
            if (items.Count % 2 != 0) throw new InvalidOperationException("Unpaired dictionary entry");
            var result = (IDictionary)Activator.CreateInstance(type); var types = type.GetGenericArguments();
            for (int i = 0; i < items.Count; i += 2) result.Add(Read(items[i], types[0], depth + 1), Read(items[i + 1], types[1], depth + 1));
            return result;
        }
        if (Sequence(type))
        {
            var items = node.Items ?? throw new InvalidOperationException("Missing sequence items");
            Type element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
            if (type.IsArray)
            {
                var array = Array.CreateInstance(element, items.Count);
                for (int i = 0; i < items.Count; i++) array.SetValue(Read(items[i], element, depth + 1), i);
                return array;
            }
            var result = Activator.CreateInstance(type); var add = type.GetMethod("Add", new[] { element });
            foreach (var item in items) add.Invoke(result, new[] { Read(item, element, depth + 1) });
            return result;
        }
        if (!_allowModel(type)) throw new InvalidOperationException("Unsupported managed model: " + type.FullName);
        var fields = Fields(type);
        if (node.Fields == null || node.Fields.Count != fields.Length) throw new InvalidOperationException("Managed model schema mismatch");
        var target = FormatterServices.GetUninitializedObject(type);
        foreach (var field in fields)
        {
            if (!node.Fields.TryGetValue(field.Name, out var value)) throw new InvalidOperationException("Missing model field: " + field.Name);
            field.SetValue(target, Read(value, field.FieldType, depth + 1));
        }
        return target;
    }
}
