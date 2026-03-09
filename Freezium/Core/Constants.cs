using System;
using System.IO;

namespace Freezium.Core
{
    /// <summary>
    /// Application-wide constants and paths.
    /// </summary>
    public static class Constants
    {
        public const string AppName = "Freezium";

        public static readonly string DataFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

        public static readonly string CertLocation =
            Path.Combine(DataFolder, "FreeziumCert.p12");

        public static readonly string DatabasePath =
            Path.Combine(DataFolder, "Freezium.db");

        public const string CertPassword = "0418";

        public const string AniziumApiBaseUrl = "https://api.anizium.co";

        public const string AniziumEncryptionKey = "16ghkdz5qnwinkyebwopbd94b49xhs";

        public const string BypassHost = "discord.com";

        public const string TargetApiHost = "api.anizium.co";

        public const int ProxyPort = 8888;
    }
}
