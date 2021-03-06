﻿//  Copyright 2015 Google Inc. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http ://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using NDesk.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TokenLibrary;

namespace DumpProcessMitigations
{
    class Program
    {
        static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Usage: DumpProcessMitigations [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

        static Dictionary<string, PropertyInfo> _props = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        

        static Dictionary<uint, string> GetCommandLines()
        {
            Dictionary<uint, string> cmdlines = new Dictionary<uint, string>();

            string wmiQuery = string.Format("select * from Win32_Process");
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(wmiQuery);
            ManagementObjectCollection retObjectCollection = searcher.Get();
            foreach (ManagementObject obj in retObjectCollection)
            {
                uint id = (uint)obj["ProcessId"];
                cmdlines.Add(id, (string)(obj["CommandLine"] ?? ""));
            }

            return cmdlines;
       }

        static bool HasPropertySet(ProcessMitigations mitigations, IEnumerable<string> props)
        {
            foreach (string propname in props)
            {
                if (_props.ContainsKey(propname))
                {
                    if ((bool)_props[propname].GetValue(mitigations))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static void FormatEntry(string name, object value)
        {
            Console.WriteLine("- {0,-45}: {1}", name, value);
        }

        static void DumpProcessEntry(ProcessEntry entry, HashSet<string> mitigation_filter, bool all_mitigations)
        {
            ProcessMitigations mitigations = entry.Mitigations;

            Console.WriteLine("Process Mitigations: {0,8} - {1}", entry.Pid, entry.Name);
            IEnumerable<PropertyInfo> props = _props.Values.Where(p => all_mitigations || mitigation_filter.Contains(p.Name));
            foreach (PropertyInfo prop in props.OrderBy(p => p.Name))
            {
                FormatEntry(prop.Name, prop.GetValue(mitigations));
            }
            Console.WriteLine();
        }

        static void Main(string[] args)
        {
            bool show_help = false;
            HashSet<string> mitigation_filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> process_filter = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            HashSet<int> pid_filter = new HashSet<int>();
            HashSet<string> cmdline_filter = new HashSet<string>();            
            bool all_mitigations = false;
            OptionSet p = new OptionSet() {
                        { "t|type=", "A filter for processes with a specific mitigation to display",  v => mitigation_filter.Add(v.Trim()) },
                        { "f|filter=", "A filter for the name of a process to display",  v => process_filter.Add(v.Trim()) },
                        { "p|pid=", "A filter for a specific PID to display", v => pid_filter.Add(int.Parse(v)) },
                        { "c|cmd=", "A filter for the command line of a process to display",  v => cmdline_filter.Add(v.Trim().ToLower()) },
                        { "a|all", "When filtering on mitigation show all process mitigations", v => all_mitigations = v != null },
                        { "h|help",  "show this message and exit", 
                           v => show_help = v != null },
                    };            

            foreach (PropertyInfo prop in typeof(ProcessMitigations).GetProperties())
            {
                _props.Add(prop.Name.ToLower(), prop);
            }

            try
            {
                p.Parse(args);

                if (show_help)
                {
                    ShowHelp(p);
                }
                else
                {
                    IEnumerable<ProcessEntry> procs = ProcessEntry.GetProcesses();

                    if (cmdline_filter.Count > 0)
                    {
                        Dictionary<uint, string> cmdlines = GetCommandLines();
                        foreach (KeyValuePair<uint, string> pair in cmdlines)
                        {
                            if ((int)pair.Key != Process.GetCurrentProcess().Id)
                            {
                                foreach (string filter in cmdline_filter)
                                {
                                    if (pair.Value.ToLower().Contains(filter))
                                    {
                                        pid_filter.Add((int)pair.Key);
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (pid_filter.Count > 0)
                    {
                        procs = procs.Where(e => pid_filter.Contains(e.Pid));
                    }

                    if (process_filter.Count > 0)
                    {
                        procs = procs.Where(e => process_filter.Contains(e.Name));
                    }

                    if (mitigation_filter.Count > 0)
                    {
                        procs = procs.Where(e => HasPropertySet(e.Mitigations, mitigation_filter));
                    }
                    else
                    {
                        all_mitigations = true;
                    }

                    foreach (ProcessEntry entry in procs)
                    {
                        DumpProcessEntry(entry, mitigation_filter, all_mitigations);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
