using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using GammaLibrary.Extensions;
using Hyperai.Messages;
using Hyperai.Messages.ConcreteModels;
using Hyperai.Relations;
using SeTuDetectorML.Model;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using Image = SixLabors.ImageSharp.Image;

namespace SeTuDetectorCore
{

    public struct SeTuTask
    {
        public Group Group;
        public string FileName;
        public bool Force;


        public bool Equals(SeTuTask other)
        {
            return Equals(Group, other.Group) && FileName == other.FileName;
        }

        public override bool Equals(object obj)
        {
            return obj is SeTuTask other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Group, FileName);
        }
    }
    public class SeTuTaskManager
    {
        private static HashSet<SeTuTask> taskQueue = new HashSet<SeTuTask>();
        private static DateTime lastSendTime = DateTime.MinValue;
        private static bool silence = false;

        private static void SendMessage(Group group, MessageChain msg)
        {
            var delta = DateTime.Now - lastSendTime;
            if (silence)
            {
                if (delta < TimeSpan.FromSeconds(30))
                {
                    return;
                }

                silence = false;
            }
            else
            {
                if (delta < TimeSpan.FromSeconds(5))
                {
                    silence = true;
                    SendMessageInternal(group, new MessageChain(new[] { new Plain("检测到可能会发送消息速度太快, 在一段时间内会停止消息发送."), }));
                    return;
                }
            }

            lastSendTime = DateTime.Now;
            SendMessageInternal(group, msg);



            void SendMessageInternal(Group g, MessageChain messageChain)
            {
                Program.MiraiHttpSession.SendGroupMessageAsync(g, messageChain).Wait();
            }
        }

        public static void ReportTask(Program.FileResult result)
        {
            if (Program.MiraiHttpSession == null) return;
            if (taskQueue.All(task => task.FileName != result.FilePath))
            {
                Console.WriteLine($"警告: {result.FilePath} 不在队列中.");
                return;
            }

            var task = taskQueue.First(task => task.FileName == result.FilePath);
            taskQueue.Remove(task);

            var path = Path.Combine("pending check", result.FilePath);

            ModelInput sampleData = new ModelInput()
            {
                ImageSource = Path.GetFullPath(path),
            };
            var predictionResult = ConsumeModel.Predict(sampleData);
            var tags_like = Enum.GetNames(typeof(TagsLike));
            if (predictionResult.Prediction == "二次元色图" || task.Force)
            {
                if (task.Force)
                {
                    var tags = result.Tags.Select(t => t.TagName).ToArray();
                    for (var i = 0; i < tags.Length; i++)
                    {
                        var tagstr = tags[i];
                        if (Enum.TryParse<TagsLike>(tagstr, out var tag))
                        {
                            tags[i] = GetChinese(tag);
                        }

                    }
                    SendMessage(task.Group, MessageChain.Construct(new Plain($"检测到含有以下元素: {tags.Connect()}, 模型匹配为{predictionResult.Prediction} (完全不准确)")));
                }
                else
                {
                    var likes = tags_like.Intersect(result.Tags.Select(t => t.TagName)).ToArray();
                    if (likes.Length == 0) return;
                    var msg =
                        (from like in likes select Enum.Parse<TagsLike>(like) into tag select GetChinese(tag)).Connect();
                    SendMessage(task.Group,
                        new MessageChain(new MessageComponent[] { new At(775942303), new Plain($"检测到爷爷您最爱的 {msg} 色图!") }));
                }
                
            }
        }

        public enum TagsLike
        {
            white_legwear,
            blue_eyes,
            red_eyes,
            green_eyes,
            animal_ears,
            silver_hair,
            white_hair,
            blonde_hair,
            pink_hair,
            breasts,
            pantyhose,
            panties
        }

        public static string GetChinese(TagsLike tag)
        {
            return tag switch
            {
                TagsLike.white_legwear => "白丝",
                TagsLike.blue_eyes => "蓝瞳",
                TagsLike.red_eyes => "红眼",
                TagsLike.green_eyes => "绿眼",
                TagsLike.animal_ears => "兽耳",
                TagsLike.silver_hair => "银发",
                TagsLike.white_hair => "白毛",
                TagsLike.blonde_hair => "银发",
                TagsLike.pink_hair => "粉毛",
                TagsLike.breasts => "奶子",
                TagsLike.pantyhose => "裤袜",
                TagsLike.panties => "胖次",
                _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, null)
            };
        }

        public static void AddTask(string filename, Group @group, bool force)
        {
            var path = Path.Combine("pending check", filename);
            var extension = GetExtension(path);
            if (extension is null) return;

            taskQueue.Add(new SeTuTask{FileName = filename, Group = group, Force = force});
            Program.process.Suspend();
            File.Copy(path, "D:\\WARNING-DELETE-IF-FILE-IN\\" + filename + extension);
            Program.process.Resume();
        }

        public static string GetExtension(string path)
        {
            path = Path.GetFullPath(path);
            switch (Image.DetectFormat(File.OpenRead(path)))
            {
                case GifFormat gifFormat:
                    return null;
                case JpegFormat jpegFormat:
                    return ".jpg";
                case PngFormat pngFormat:
                    return ".png";
                default:
                    return null;
            }
        }
    }

    public static class ProcessExtension
    {

        [Flags]
        public enum ThreadAccess : int
        {
            TERMINATE = (0x0001),
            SUSPEND_RESUME = (0x0002),
            GET_CONTEXT = (0x0008),
            SET_CONTEXT = (0x0010),
            SET_INFORMATION = (0x0020),
            QUERY_INFORMATION = (0x0040),
            SET_THREAD_TOKEN = (0x0080),
            IMPERSONATE = (0x0100),
            DIRECT_IMPERSONATION = (0x0200)
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
        [DllImport("kernel32.dll")]
        static extern uint SuspendThread(IntPtr hThread);
        [DllImport("kernel32.dll")]
        static extern int ResumeThread(IntPtr hThread);

        public static void Suspend(this Process process)
        {
            foreach (ProcessThread thread in process.Threads)
            {
                var pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);
                if (pOpenThread == IntPtr.Zero)
                {
                    break;
                }
                SuspendThread(pOpenThread);
            }
        }
        public static void Resume(this Process process)
        {
            foreach (ProcessThread thread in process.Threads)
            {
                var pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);
                if (pOpenThread == IntPtr.Zero)
                {
                    break;
                }
                ResumeThread(pOpenThread);
            }
        }
    }
}
