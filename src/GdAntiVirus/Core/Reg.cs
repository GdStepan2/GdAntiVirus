using System;
using Microsoft.Win32;

namespace GdAntiVirus.Core
{
    /// <summary>Registry helpers that always use the 64-bit view.</summary>
    public static class Reg
    {
        public static RegistryKey Root(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

        public static object Get(RegistryHive hive, string path, string name)
        {
            try
            {
                using (var k = Root(hive).OpenSubKey(path))
                    return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            }
            catch { return null; }
        }

        public static int? GetInt(RegistryHive hive, string path, string name)
        {
            var v = Get(hive, path, name);
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is string s && int.TryParse(s, out var p)) return p;
            return null;
        }

        public static string GetString(RegistryHive hive, string path, string name) => Get(hive, path, name)?.ToString();

        public static void SetInt(RegistryHive hive, string path, string name, int value)
        {
            using (var k = Root(hive).CreateSubKey(path, true))
                k.SetValue(name, value, RegistryValueKind.DWord);
        }

        public static void SetString(RegistryHive hive, string path, string name, string value)
        {
            using (var k = Root(hive).CreateSubKey(path, true))
                k.SetValue(name, value, RegistryValueKind.String);
        }

        public static void SetBinary(RegistryHive hive, string path, string name, byte[] value)
        {
            using (var k = Root(hive).CreateSubKey(path, true))
                k.SetValue(name, value, RegistryValueKind.Binary);
        }

        public static void Delete(RegistryHive hive, string path, string name)
        {
            using (var k = Root(hive).OpenSubKey(path, true))
                k?.DeleteValue(name, false);
        }

        public static bool KeyExists(RegistryHive hive, string path)
        {
            try { using (var k = Root(hive).OpenSubKey(path)) return k != null; }
            catch { return false; }
        }

        public static void DeleteTree(RegistryHive hive, string path)
        {
            try { Root(hive).DeleteSubKeyTree(path, false); } catch (ArgumentException) { }
        }
    }
}
