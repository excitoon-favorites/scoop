using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Scoop {

    class Program {
        static int Main(string[] args) {
            var exe = Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(exe);
            var name = Path.GetFileNameWithoutExtension(exe);

            var configPath = Path.Combine(dir, name + ".shim");
            if(!File.Exists(configPath)) {
                Console.Error.WriteLine("Couldn't find " + Path.GetFileName(configPath) + " in " + dir);
                return 1;
            }

            var config = Config(configPath);
            var path = Get(config, "path");
            var add_args = Get(config, "args");

            // create command line
            var cmd_args = add_args ?? "";
            var pass_args = GetArgs(Environment.CommandLine);
            if(!string.IsNullOrEmpty(pass_args)) {
                if(!string.IsNullOrEmpty(cmd_args)) cmd_args += " ";
                cmd_args += pass_args;
            }
            if(!string.IsNullOrEmpty(cmd_args)) cmd_args = " " + cmd_args;
            var cmd = "\"" + path + "\"" + cmd_args;

            Process process = new Process();
            process.StartInfo = new ProcessStartInfo(path, cmd_args);
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            try {
                process.Start();
            }
            catch(Win32Exception exception) {
                return exception.ErrorCode;
            }
            Stream input = Console.OpenStandardInput(0);
            Task forward_input = new Task(() => { RedirectStream(new StreamReader(input), process.StandardInput); });
            Task forward_output = new Task(() => { RedirectStream(process.StandardOutput, Console.Out); });
            Task forward_error = new Task(() => { RedirectStream(process.StandardError, Console.Error); });
            forward_input.Start();
            forward_output.Start();
            forward_error.Start();
            process.WaitForExit();
            input.Close();
            return process.ExitCode;
        }

        static void RedirectStream(TextReader from, TextWriter to)
        {
            char[] buffer = new char[4096];
            try {
                while(true) {
                    int size = from.Read(buffer, 0, buffer.Length);
                    if(size <= 0) {
                        break;
                    }
                    to.Write(buffer, 0, size);
                    to.Flush();
                }
            }
            catch(System.Exception) {
                // Do nothing.
            }
            to.Close();
        }

        // now uses GetArgs instead
        static string Serialize(string[] args) {
            return string.Join(" ", args.Select(a => a.Contains(' ') ? '"' + a + '"' : a));
        }

        // strips the program name from the command line, returns just the arguments
        static string GetArgs(string cmdLine) {
            if(cmdLine.StartsWith("\"")) {
                var endQuote = cmdLine.IndexOf("\" ", 1);
                if(endQuote < 0) return "";
                return cmdLine.Substring(endQuote + 1);
            }
            var space = cmdLine.IndexOf(' ');
            if(space < 0 || space == cmdLine.Length - 1) return "";
            return cmdLine.Substring(space + 1);
        }

        static string Get(Dictionary<string, string> dic, string key) {
            string value = null;
            dic.TryGetValue(key, out value);
            return value;
        }

        static Dictionary<string, string> Config(string path) {
            var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach(var line in File.ReadAllLines(path)) {
                var m = Regex.Match(line, @"([^=]+)=(.*)");
                if(m.Success) {
                    config[m.Groups[1].Value.Trim()] = m.Groups[2].Value.Trim();
                }
            }
            return config;
        }
    }
}
