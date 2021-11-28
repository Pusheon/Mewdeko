using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Serilog;

namespace Mewdeko.Modules.Utility.Services
{
    public class DllVersionChecker
    {
        public string GetDllVersion(string dllName = null)
        {
            try
            {
                var dllPath = Convert.ToString(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
                var testPath = $"{dllPath}/Discord.Net.Commands.dll";
                if (dllName != null)
                {
                    testPath = $"{dllPath}/{dllName}";
                }

                var myFileVersionInfo = FileVersionInfo.GetVersionInfo(testPath);
                return $"{myFileVersionInfo.FileMajorPart}.{myFileVersionInfo.FileMinorPart}.{myFileVersionInfo.FilePrivatePart}";
            }
            catch
            {
                Log.Error("Unable to find version number of requested dll");
                return null;
            }
        }
    }
}