using ERASimulator.Errors;
using ERASimulator.Modules;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

internal sealed class NativeMethods
{
    [DllImport("kernel32.dll")]
    public static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    public static extern bool FreeConsole();
}

namespace ERASimulator
{
    class Program
    {
        private static List<string> binaryFilenames; // File/Files with bincode to be simulated
        private static List<string> dumpFilenames; // File/Files for memory dumps
        private static List<string> printFilenames; // File/Files for "print" output

        private static bool forceFolderCreation = false;
        
        public static string currentFile = "none";        
        public static bool showTrace = false;        
        public static bool noDump = false;        
        public static uint bytesToAllocate = 16 * 1024 * 1024; // 16 MB

        static void Main(string[] args)
        {

            //args = new string[] { "-s", "actual_compiled_basic_2.bin", "--mb", "3" };

            NativeMethods.AllocConsole();

            bool error = true;
            try
            {
                error = ProcessArguments(args);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Console.WriteLine(ex.Message);
            }
            if (error)
            {
                NativeMethods.FreeConsole();
                return;
            }

            for (int i = 0; i < binaryFilenames.Count; i++)
            {
                try
                {
                    currentFile = binaryFilenames[i];

                    // Loading of compiled code
                    byte[] binaryCode = File.ReadAllBytes(binaryFilenames[i]);

                    // For time tracking
                    Stopwatch stopWatch = new Stopwatch();

                    // Create instance of the ERA simulator and get the memory dump 
                    // It is fresh everytime to refresh all the nodes (may be optimized obviously)                    
                    stopWatch.Start();
                    string memoryDump = "";
                    memoryDump = new Simulator(binaryCode).Simulate();
                    stopWatch.Stop();

                    TimeSpan ts = stopWatch.Elapsed;
                    string elapsedTime = string.Format("{0:00}m {1:00}.{2:00}s",
                    ts.Minutes, ts.Seconds, ts.Milliseconds / 10);

                    // Create a new file with the memory dump
                    if (i >= dumpFilenames.Count)
                    {
                        string defaultFilename = binaryFilenames[i];
                        // If it is a path
                        if (defaultFilename.Contains("/") || defaultFilename.Contains("\\"))
                        {
                            int j = defaultFilename.LastIndexOfAny(new char[] { '\\', '/' });
                            defaultFilename = defaultFilename.Insert(j + 1, "dump_");
                        }
                        else
                        {
                            defaultFilename = "dump_" + defaultFilename;
                        }
                        int dot = defaultFilename.LastIndexOf('.');
                        defaultFilename = defaultFilename.Remove(dot); // Remove .* and add .dmp
                        defaultFilename += ".dmp";

                        dumpFilenames.Add(defaultFilename);
                    }
                    
                    if (dumpFilenames[i].Contains("/") || dumpFilenames[i].Contains("\\"))
                    {
                        // Check if directory exists
                        string folder = dumpFilenames[i].Remove(dumpFilenames[i].LastIndexOfAny(new char[] { '\\', '/' }));
                        if (Directory.Exists(folder))
                        {
                            if (!noDump)
                                File.WriteAllText(dumpFilenames[i], memoryDump);
                            Console.WriteLine("\"" + binaryFilenames[i] + "\" has been simulated (" + elapsedTime + ").");
                        }
                        else
                        {
                            if (forceFolderCreation)
                            {
                                if (!noDump)
                                {
                                    Directory.CreateDirectory(folder);
                                    File.WriteAllText(dumpFilenames[i], memoryDump);
                                }
                                Console.WriteLine("\"" + binaryFilenames[i] + "\" has been simulated (" + elapsedTime + ").");
                            }
                            else
                            {
                                Console.WriteLine("Folder \"" + folder + "\" does not exists!!!");
                            }
                        }
                    }
                    else
                    {
                        if (!noDump)
                            File.WriteAllText(dumpFilenames[i], memoryDump);
                        Console.WriteLine("\"" + binaryFilenames[i] + "\" has been simulated (" + elapsedTime + ").");
                    }

                    // Create a new file with the "print" output
                    if (i >= printFilenames.Count)
                    {
                        string defaultPrintFilename = binaryFilenames[i];
                        // If it is a path
                        if (defaultPrintFilename.Contains("/") || defaultPrintFilename.Contains("\\"))
                        {
                            int j = defaultPrintFilename.LastIndexOfAny(new char[] { '\\', '/' });
                            defaultPrintFilename = defaultPrintFilename.Insert(j + 1, "print_");
                        }
                        else
                        {
                            defaultPrintFilename = "print_" + defaultPrintFilename;
                        }
                        int dot = defaultPrintFilename.LastIndexOf('.');
                        defaultPrintFilename = defaultPrintFilename.Remove(dot); // Remove .* and add .eralog
                        defaultPrintFilename += ".eralog";

                        printFilenames.Add(defaultPrintFilename);
                    } 

                    if (printFilenames[i].Contains("/") || printFilenames[i].Contains("\\"))
                    {
                        // Check if directory exists
                        string folder = printFilenames[i].Remove(printFilenames[i].LastIndexOfAny(new char[] { '\\', '/' }));
                        if (Directory.Exists(folder))
                        {
                            File.WriteAllText(printFilenames[i], Simulator.printTrace.ToString());
                        }
                        else
                        {
                            if (forceFolderCreation)
                            {
                                Directory.CreateDirectory(folder);
                                File.WriteAllText(printFilenames[i], Simulator.printTrace.ToString());
                            }
                            else
                            {
                                Console.WriteLine("Folder \"" + folder + "\" does not exists!!!");
                            }
                        }
                    }
                    else
                    {
                        File.WriteAllText(printFilenames[i], Simulator.printTrace.ToString());
                    }
                }
                catch (IOException ex)
                {
                    Console.WriteLine(ex.Message);
                    throw;
                }
                catch (SimulationErrorException ex)
                {
                    Console.WriteLine(ex.Message);
                    File.WriteAllText("error_dump.dmp", Simulator.executionTrace.ToString() + "\r\n\r\n" + Simulator.printTrace.ToString());
                    throw;
                }
            }

            NativeMethods.FreeConsole();
        }

        private static bool ProcessArguments(string[] args)
        {
            binaryFilenames = new List<string>();
            dumpFilenames = new List<string>();
            printFilenames = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                if (IsFlag(args[i]))
                {
                    switch (args[i])
                    {
                        case "-s":
                            {
                                // No files found
                                if (i == args.Length - 1 || IsFlag(args[i + 1]))
                                {
                                    Console.Error.WriteLine("No binary files specified!!!");
                                    return true;
                                }
                                i++;
                                while (i < args.Length && !IsFlag(args[i]))
                                {
                                    binaryFilenames.Add(args[i]);
                                    i++;
                                }
                                i--;
                                break;
                            }
                        case "-d":
                            {
                                // No folders found
                                if (i == args.Length - 1 || IsFlag(args[i + 1]))
                                {
                                    Console.Error.WriteLine("No binary folders specified!!!");
                                    return true;
                                }
                                i++;
                                while (i < args.Length && !IsFlag(args[i]))
                                {
                                    string[] files = Directory.GetFiles(args[i]);
                                    foreach (string filename in files)
                                    {
                                        binaryFilenames.Add(filename);
                                    }
                                    i++;
                                }
                                i--;
                                break;
                            }
                        case "-o":
                            {
                                // No files found
                                if (i == args.Length - 1 || IsFlag(args[i + 1]))
                                {
                                    Console.Error.WriteLine("No dump files specified!!!");
                                    return true;
                                }
                                i++;
                                while (i < args.Length && !IsFlag(args[i]))
                                {
                                    dumpFilenames.Add(args[i]);
                                    i++;
                                }
                                i--;
                                break;
                            }
                        case "-p":
                            {
                                forceFolderCreation = true;
                                break;
                            }
                        case "--op":
                            {
                                // No files found
                                if (i == args.Length - 1 || IsFlag(args[i + 1]))
                                {
                                    Console.Error.WriteLine("No print files specified!!!");
                                    return true;
                                }
                                i++;
                                while (i < args.Length && !IsFlag(args[i]))
                                {
                                    printFilenames.Add(args[i]);
                                    i++;
                                }
                                i--;
                                break;
                            }
                        case "--b":
                            {
                                if (i == args.Length - 1 || IsFlag(args[i + 1]))
                                {
                                    Console.Error.WriteLine("No bytes specified!!!");
                                    return true;
                                }
                                i++;
                                bytesToAllocate = uint.Parse(args[i]);
                                break;
                            }
                        case "--kb":
                            {
                                if (i == args.Length - 1 || IsFlag(args[i + 1]))
                                {
                                    Console.Error.WriteLine("No kilobytes specified!!!");
                                    return true;
                                }
                                i++;
                                bytesToAllocate = uint.Parse(args[i]) * 1024;
                                break;
                            }
                        case "--mb":
                            {
                                if (i == args.Length - 1 || IsFlag(args[i + 1]))
                                {
                                    Console.Error.WriteLine("No megabytes specified!!!");
                                    return true;
                                }
                                i++;
                                bytesToAllocate = uint.Parse(args[i]) * 1024 * 1024;
                                break;
                            }
                        case "--trace":
                            {
                                showTrace = !noDump;
                                break;
                            }
                        case "--nodump":
                            {
                                showTrace = false;
                                noDump = true;
                                break;
                            }
                        case "-h":
                            {
                                Console.Error.WriteLine(
                                    "      ERA SIMULATOR\r\n" +
                                    "  INNOPOLIS UNIVERSITY\r\n" +
                                    "\r\n" +
                                    "  Possible arguments:\r\n" +
                                    "  '-s' { filepath }  :  specify binary files to be simulated\r\n" +
                                    "  '-d' { path }  :  specify the folders with binary files to be simulated\r\n" +
                                    "  '-o' { filepath }  :  specify memory dump files\r\n" +
                                    "  '-p'  :  force to create folders if they do not exist\r\n" +
                                    "  '--op' { filepath }  :  specify print files\r\n" +
                                    "  '--b' <# of bytes>  :  specify how much bytes to allocate for memory (default is 16 MB)\r\n" +
                                    "  '--kb' <# of kilobytes>  :  specify how much kilobytes\r\n" +
                                    "  '--mb' <# of megabytes>  :  specify how much megabytes\r\n" +
                                    "  '--trace'  :  show all execution steps (at the end of memdump file)\r\n" +
                                    "  '--nodump'  : disable dump file generation\r\n" +
                                    "  '-h'  :  show manual\r\n" +
                                    "  Default binary file is 'compiled_code.bin'.\r\n" +
                                    "  Default dump file is 'dump_' + binary code file name + '.dmp'\r\n" +
                                    "  Print files are 'print_' + binary code file name + '.eralog'\r\n"
                                    );
                                break;
                            }
                        default:
                            {
                                throw new SimulationErrorException("Unknown parameter\"" + args[i] + "\" !!!");
                            }
                    }
                }
            }

            return false;
        }

        private static bool IsFlag(string s)
        {
            return s.StartsWith("-") || s.StartsWith("--");
        }
    }
}
