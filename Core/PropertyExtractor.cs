using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace PHDNavisTools.Core
{
    public class PropertyExtractor
    {
        private static readonly HashSet<string> IgnoredCategories = new HashSet<string>
        {
            "Autodesk.Navisworks.Node",
            "Autodesk.Navisworks.Internal",
        };

        public Dictionary<string, Dictionary<string, string>> Extract(ModelItem item)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();

            foreach (var category in item.PropertyCategories)
            {
                if (IgnoredCategories.Contains(category.Name))
                    continue;

                var psetName = SanitizeName(category.DisplayName ?? category.Name);
                var props = new Dictionary<string, string>();

                foreach (var property in category.Properties)
                {
                    var propName  = SanitizeName(property.DisplayName ?? property.Name);
                    var propValue = SafeString(property.Value);
                    if (propName.Length > 0)
                        props[propName] = propValue;
                }

                if (props.Count > 0)
                    result[psetName] = props;
            }

            return result;
        }

        // Converte qualquer VariantData para string sem lançar exceção.
        // ToDisplayString() só funciona para DataType.DisplayString — os outros
        // tipos têm seus próprios métodos To*().
        internal static string SafeString(VariantData? value)
        {
            if (value == null || value.IsNone)
                return string.Empty;

            try
            {
                if (value.IsDisplayString)    return value.ToDisplayString();
                if (value.IsIdentifierString) return value.ToIdentifierString();
                if (value.IsBoolean)          return value.ToBoolean().ToString();
                if (value.IsInt32)            return value.ToInt32().ToString();
                if (value.IsInt64)            return value.ToInt64().ToString();
                if (value.IsNat32)            return value.ToNat32().ToString();
                if (value.IsNat64)            return value.ToNat64().ToString();
                if (value.IsAnyDouble)        return value.ToAnyDouble().ToString("G");
                if (value.IsDateTime)         return value.ToDateTime().ToString("s");
                if (value.IsNamedConstant)
                {
                    var nc = value.ToNamedConstant();
                    return nc.DisplayName ?? nc.Name ?? string.Empty;
                }
                if (value.IsPoint3D)
                {
                    var p = value.ToPoint3D();
                    return $"({p.X:G}, {p.Y:G}, {p.Z:G})";
                }
                if (value.IsPoint2D)
                {
                    var p = value.ToPoint2D();
                    return $"({p.X:G}, {p.Y:G})";
                }

                value.ToSerializableString(out var fallback);
                return fallback ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        // IFC allows spaces in names; only strip characters that break the STEP parser.
        private static string SanitizeName(string name) =>
            name.Replace("'", "").Replace("\\", "").Replace("\r", "").Replace("\n", "").Trim();
    }
}
