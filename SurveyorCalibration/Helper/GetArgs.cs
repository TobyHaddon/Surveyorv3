using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Surveyor.Helper
{
    static public class GetArgs
    {      
        /// <summary>
        /// Get a specific command line argument by name
        /// e.g. /StereoFile="C:\path store\to\file.txt"       
        /// </summary>
        /// <param name="argName"></param>
        /// <param name="argValue"></param>
        /// <returns>true if found</returns>
        public static bool GetArg(string argName, out string argValue, bool removeQuotes)
        {
            // Get the command line arguments
            var args = Environment.GetCommandLineArgs();
            // Find the argument
            var arg = args.FirstOrDefault(a => a.StartsWith(argName + "="));
            if (arg != null)
            {
                // Extract the value
                argValue = arg.Substring(argName.Length + 1).Trim();

                if (removeQuotes && argValue.StartsWith("\"") && argValue.EndsWith("\""))
                {
                    // Remove the quotes if they are present
                    argValue = argValue.Substring(1, argValue.Length - 2);
                }

                return true;
            }
            else
            {
                argValue = string.Empty;
                return false;
            }
        }


        /// <summary>
        /// Get a specific command line argument by name
        /// e.g. /StartFrame=123
        /// </summary>
        /// <param name="argName"></param>
        /// <param name="argValue"></param>
        /// <returns></returns>
        public static bool GetArg(string argName, out int? argValue)
        {
            // Get the command line arguments
            var args = Environment.GetCommandLineArgs();
            // Find the argument
            var arg = args.FirstOrDefault(a => a.StartsWith(argName + "="));
            if (arg != null)
            {
                // Extract the value
                var valueStr = arg.Substring(argName.Length + 1).Trim();
                
                bool ret = int.TryParse(valueStr, out int tempValue);
                if (ret == true)
                {
                    argValue = tempValue;
                    return true;
                }
                else
                {
                    argValue = null;
                    return false;
                }
            }
            else
            {
                argValue = 0;
                return false;
            }
        }


        /// <summary>
        /// Get a specific command line argument by name
        /// e.g. /zoom=1.5
        /// </summary>
        /// <param name="argName"></param>
        /// <param name="argValue"></param>
        /// <returns></returns>
        public static bool GetArg(string argName, out double? argValue)
        {
            // Get the command line arguments
            var args = Environment.GetCommandLineArgs();
            // Find the argument
            var arg = args.FirstOrDefault(a => a.StartsWith(argName + "="));
            if (arg != null)
            {
                // Extract the value
                var valueStr = arg.Substring(argName.Length + 1).Trim();
                
                bool ret = double.TryParse(valueStr, out double tempValue);
                if (ret == true)
                {
                    argValue = tempValue;
                }
                else
                {
                    argValue = null;
                }
                return ret;
            }
            else
            {
                argValue = null;
                return false;
            }
        }

        /// <summary>
        /// Get a specific command line argument by name
        /// e.g. /Stereo= true or just /Stereo
        /// </summary>
        /// <param name="argName"></param>
        /// <param name="argValue"></param>
        /// <returns></returns>
        public static bool GetArg(string argName, out bool? argValue)
        {
            // Get the command line arguments
            var args = Environment.GetCommandLineArgs();
            // Find the argument
            var arg = args.FirstOrDefault(a => a.StartsWith(argName + "="));
            if (arg != null)
            {
                // Extract the value
                var valueStr = arg.Substring(argName.Length + 1).Trim();
                bool ret = bool.TryParse(valueStr, out bool tempValue);
                if (ret == true)
                {
                    argValue = tempValue;
                    return true;
                }
                else
                {
                    argValue = null;
                    return false;
                }
            }
            else
            {
                var arg2 = args.FirstOrDefault(a => a.Equals(argName));
                if (arg2 != null)
                {
                    argValue = true;
                    return true;
                }
                else
                {
                    argValue = null;
                    return false;
                }
            }
        }
    }
}
