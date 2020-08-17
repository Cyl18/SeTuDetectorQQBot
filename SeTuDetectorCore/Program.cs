using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Ac682.Hyperai.Clients.Mirai;
using GammaLibrary.Extensions;
using Hyperai.Events;
using Hyperai.Messages;
using Hyperai.Messages.ConcreteModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeTuDetectorCore
{
    public class Program
    {
        public static MiraiHttpSession MiraiHttpSession;
        public static Process process;

        public struct Tag
        {
            public string Score;
            public string TagName;
        }

        public struct FileResult
        {
            public string FilePath;
            public List<Tag> Tags;
        }

        static void Main()
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "-u -m deepdanbooru evaluate --project-path D:\\DeepDanbooru-master\\test --allow-folder",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            var dic = new Dictionary<string, int>();
            var count = new List<int>();

            process.OutputDataReceived += (sender, eventArgs) =>
            {
                var line = eventArgs.Data;
                if (line.IsNullOrWhiteSpace() || !line.StartsWith("MAGIC")) return;
                line = line.Substring(5);
                var split = line.Split('艹');
                var path = split[0];
                var tagsSource = split.Skip(1);
                var tags = new List<Tag>();

                foreach (var tagSource in tagsSource)
                {
                    var split1 = tagSource.Split('丂');
                    var score = split1[0];
                    var tag = split1[1];
                    tags.Add(new Tag { Score = score, TagName = tag });
                    if (!dic.ContainsKey(tag)) dic[tag] = 0;

                    dic[tag]++;
                }
                count.Add(tags.Count);
                var result = new FileResult { FilePath = Path.GetFileNameWithoutExtension(path), Tags = tags };
                SeTuTaskManager.ReportTask(result);
            };

            ChildProcessTracker.AddProcess(process);
            process.BeginOutputReadLine();


            MiraiHttpSession = new MiraiHttpSession("127.0.0.1", 8080, "********", 3320645904);
            MiraiHttpSession session = MiraiHttpSession;
            session.Connect();
            while (true)
            {
                var evt = session.PullEvent();
                if (evt is GroupMessageEventArgs args)
                {
                    var force = args.Message.OfType<Plain>().Any(m => m.Text.Contains("/色图检测"));

                    foreach (var component in args.Message)
                    {
                        if (component is Image image)
                        {
                            using var result = image.OpenReadAsync().Result;
                            var tempfilename = Guid.NewGuid().ToString("D");
                            var temppath = Path.Combine("pending check", tempfilename);
                            Stream s = File.Create(temppath);

                            result.CopyTo(s);
                            // 写重复检测
                            s.Close();
                            
                            var filename = File.ReadAllBytes(temppath).MD5().ToHexString();
                            var realpath = Path.Combine("pending check", filename);
                            if (File.Exists(realpath) && !force) continue;
                            
                            File.Move(temppath, realpath, true);
                            SeTuTaskManager.AddTask(filename, args.Group, force);
                        }
                    }
                }
                Thread.Sleep(50);
            }
        }
    }


    public static class ChildProcessTracker
    {
        /// <summary>
        /// Add the process to be tracked. If our current process is killed, the child processes
        /// that we are tracking will be automatically killed, too. If the child process terminates
        /// first, that's fine, too.</summary>
        /// <param name="process"></param>
        public static void AddProcess(Process process)
        {
            if (s_jobHandle != IntPtr.Zero)
            {
                bool success = AssignProcessToJobObject(s_jobHandle, process.Handle);
                if (!success && !process.HasExited)
                    throw new Win32Exception();
            }
        }

        static ChildProcessTracker()
        {
            // This feature requires Windows 8 or later. To support Windows 7 requires
            //  registry settings to be added if you are using Visual Studio plus an
            //  app.manifest change.
            //  https://stackoverflow.com/a/4232259/386091
            //  https://stackoverflow.com/a/9507862/386091
            if (Environment.OSVersion.Version < new Version(6, 2))
                return;

            // The job name is optional (and can be null) but it helps with diagnostics.
            //  If it's not null, it has to be unique. Use SysInternals' Handle command-line
            //  utility: handle -a ChildProcessTracker
            string jobName = "ChildProcessTracker" + Process.GetCurrentProcess().Id;
            s_jobHandle = CreateJobObject(IntPtr.Zero, jobName);

            var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION();

            // This is the key flag. When our process is killed, Windows will automatically
            //  close the job handle, and when that happens, we want the child processes to
            //  be killed, too.
            info.LimitFlags = JOBOBJECTLIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            extendedInfo.BasicLimitInformation = info;

            int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);

                if (!SetInformationJobObject(s_jobHandle, JobObjectInfoType.ExtendedLimitInformation,
                    extendedInfoPtr, (uint)length))
                {
                    throw new Win32Exception();
                }
            }
            finally
            {
                Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string name);

        [DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(IntPtr job, JobObjectInfoType infoType,
            IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        // Windows will automatically close any open job handles when our process terminates.
        //  This can be verified by using SysInternals' Handle utility. When the job handle
        //  is closed, the child processes will be killed.
        private static readonly IntPtr s_jobHandle;
    }

    public enum JobObjectInfoType
    {
        AssociateCompletionPortInformation = 7,
        BasicLimitInformation = 2,
        BasicUIRestrictions = 4,
        EndOfJobTimeInformation = 6,
        ExtendedLimitInformation = 9,
        SecurityLimitInformation = 5,
        GroupInformation = 11
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public Int64 PerProcessUserTimeLimit;
        public Int64 PerJobUserTimeLimit;
        public JOBOBJECTLIMIT LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public UInt32 ActiveProcessLimit;
        public Int64 Affinity;
        public UInt32 PriorityClass;
        public UInt32 SchedulingClass;
    }

    [Flags]
    public enum JOBOBJECTLIMIT : uint
    {
        JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS
    {
        public UInt64 ReadOperationCount;
        public UInt64 WriteOperationCount;
        public UInt64 OtherOperationCount;
        public UInt64 ReadTransferCount;
        public UInt64 WriteTransferCount;
        public UInt64 OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
