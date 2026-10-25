// API keys for client connections, kept in Windows Credential Manager (Web Credentials, resource "PhoenixLAI.Connections",
// user name = connection id) and never on disk by us. C# 5 only. No P/Invoke: the Windows vault (PasswordVault) is a
// Windows Runtime class every Windows 10/11 install has, reached by name through reflection.
using System;
using System.Reflection;

namespace Phoenix
{
    public static class CredStore
    {
        public const string Resource = "PhoenixLAI.Connections";

        static Type VaultType { get { return Type.GetType("Windows.Security.Credentials.PasswordVault, Windows.Security.Credentials, ContentType=WindowsRuntime", true); } }
        static Type CredType { get { return Type.GetType("Windows.Security.Credentials.PasswordCredential, Windows.Security.Credentials, ContentType=WindowsRuntime", true); } }

        static object Call(Type t, object target, string method, params object[] args)
        {
            try { return t.GetMethod(method).Invoke(target, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        // Stores (or replaces) the key for a connection.
        public static void Write(string id, string secret) { Write(Resource, id, secret); }

        public static void Write(string resource, string id, string secret)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("connection id required");
            Delete(resource, id);
            if (string.IsNullOrEmpty(secret)) return;
            object vault = Activator.CreateInstance(VaultType);
            object cred = Activator.CreateInstance(CredType, new object[] { resource, id, secret });
            Call(VaultType, vault, "Add", cred);
        }

        // The key, or null when none is stored.
        public static string Read(string id) { return Read(Resource, id); }

        public static string Read(string resource, string id)
        {
            object cred = Find(resource, id);
            if (cred == null) return null;
            Call(CredType, cred, "RetrievePassword");
            return (string)CredType.GetProperty("Password").GetValue(cred, null);
        }

        public static bool Has(string id) { return Find(Resource, id) != null; }

        public static void Delete(string id) { Delete(Resource, id); }

        public static void Delete(string resource, string id)
        {
            object cred = Find(resource, id);
            if (cred != null) Call(VaultType, Activator.CreateInstance(VaultType), "Remove", cred);
        }

        static object Find(string resource, string id)
        {
            object vault = Activator.CreateInstance(VaultType);
            try { return Call(VaultType, vault, "Retrieve", resource, id); }
            catch (Exception) { return null; }   // "Element not found": no key stored
        }
    }
}
