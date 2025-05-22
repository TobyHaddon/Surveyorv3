using Emgu.CV.Flann;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Surveyor.User_Controls;

namespace Surveyor.Helper
{
    public class DumpClassPropertiesHelper
    {
        public static void DumpAllProperties(object obj, Reporter? report, string? ignorePropertiesCsv = null, string? includePropertiesCsv = null, int indentLevel = 0)
        {
            var indent = new string(' ', indentLevel * 2);
            var type = obj.GetType();

            report?.Info("", $"{indent}--- Start Dumping members for {type.Name} ---");

            HashSet<string>? ignoreList = null;
            HashSet<string>? includeList = null;

            if (!string.IsNullOrWhiteSpace(includePropertiesCsv))
            {
                includeList = new HashSet<string>(
                    includePropertiesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase
                );

                if (!string.IsNullOrWhiteSpace(ignorePropertiesCsv))
                {
                    report?.Info("", $"{indent}[Warning] Both include and ignore lists provided. 'include' list takes precedence.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(ignorePropertiesCsv))
            {
                ignoreList = new HashSet<string>(
                    ignorePropertiesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase
                );
            }

            // === Properties ===
            var allProperties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var prop in allProperties)
            {
                string name = prop.Name;
                bool isDeclaredHere = prop.DeclaringType == type;
                bool isExplicitlyIncluded = includeList?.Contains(name) == true;

                if (!isDeclaredHere && includeList == null)
                    continue;

                if (includeList != null && !includeList.Contains(name))
                    continue;

                if (ignoreList != null && ignoreList.Contains(name))
                    continue;

                try
                {
                    var value = prop.GetValue(obj, null);
                    report?.Info("", $"{indent}{name} (Property) = {value ?? "null"}");

                    // Recursive Dump
                    //var method = value?.GetType().GetMethod("DumpAllProperties", BindingFlags.Public | BindingFlags.Instance);
                    //if (method != null && method.GetParameters().Length == 0)
                    //{
                    //    Debug.WriteLine($"{indent}↳ Calling DumpAllProperties on {name}:");
                    //    method.Invoke(value, null);
                    //}
                }
                catch (Exception ex)
                {
                    report?.Warning("", $"{indent}{name} (Property) = [Error reading: {ex.Message}]");
                }
            }

            // === Fields ===
            var allFields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in allFields)
            {
                string name = field.Name;
                bool isDeclaredHere = field.DeclaringType == type;
                bool isExplicitlyIncluded = includeList?.Contains(name) == true;

                if (!isDeclaredHere && includeList == null)
                    continue;

                if (includeList != null && !includeList.Contains(name))
                    continue;

                if (ignoreList != null && ignoreList.Contains(name))
                    continue;

                try
                {
                    var value = field.GetValue(obj);

                    // Let's not display the XAML controls
                    if (value?.GetType().FullName?.StartsWith("Microsoft.UI.Xaml.") == true)
                        continue;

                    report?.Info("", $"{indent}{name} (Field) = {value ?? "null"}");

                    // Recursive Dump
                    //var method = value?.GetType().GetMethod("DumpAllProperties", BindingFlags.Public | BindingFlags.Instance);
                    //if (method != null && method.GetParameters().Length == 0)
                    //{
                    //    Debug.WriteLine($"{indent}↳ Calling DumpAllProperties on {name}:");
                    //    method.Invoke(value, null);
                    //}
                }
                catch (Exception ex)
                {
                    report?.Warning("", $"{indent}{name} (Field) = [Error reading: {ex.Message}]");
                }
            }

            report?.Info("", $"{indent}--- End Dumping members for {type.Name} ---");

        }

    }
}
