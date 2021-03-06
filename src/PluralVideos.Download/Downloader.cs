﻿using PluralVideos.Download.Entities;
using PluralVideos.Download.Extensions;
using PluralVideos.Download.Helpers;
using PluralVideos.Download.Options;
using PluralVideos.Download.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PluralVideos.Download
{
    public class Downloader
    {
        private readonly DownloaderOptions options;

        private readonly PluralsightService pluralsightService;

        private readonly DownloadClient downloadClient;

        public Downloader(DownloaderOptions options)
        {
            this.options = options;
            pluralsightService = new PluralsightService();
            downloadClient = new DownloadClient(options.Timeout);
        }

        public async Task Download()
        {
            if (options.ListClip || options.ListModule)
                Utils.WriteRedText("--list cannot be used with --clip or --module");
            else if (options.DownloadClip)
                await DownloadSingleClipAsync();
            else if (options.DownloadModule)
                await DownloadSingleModuleAsync();
            else
                await DownloadCompleteCourseAsync();
        }

        private async Task DownloadCompleteCourseAsync()
        {
            var course = await GetCourseAsync(options.CourseId, options.ListCourse);

            Utils.WriteYellowText($"Downloading '{course.Header.Title}' started ...");

            foreach (var (module, index) in course.Modules.WithIndex())
                await GetModuleAsync(course.Header, module, index, options.ListCourse);

            Utils.WriteYellowText($"Downloading '{course.Header.Title}' completed.");
        }

        private async Task DownloadSingleModuleAsync()
        {
            var course = await GetCourseAsync(options.CourseId, list: false);
            var (module, index) = course.Modules.WithIndex()
                .FirstOrDefault(i => i.item.Id == options.ModuleId);

            Utils.WriteYellowText($"Downloading from course'{course.Header.Title}' started ...");

            await GetModuleAsync(course.Header, module, index, list: false);

            Utils.WriteYellowText($"Download complete");
        }

        private async Task DownloadSingleClipAsync()
        {
            var course = await GetCourseAsync(options.CourseId, list: false);
            var (clip, index, title) = course.GetClip(options.ClipId);

            Utils.WriteYellowText($"Downloading from course'{course.Header.Title}' started ...");
            Utils.WriteGreenText($"\tDownloading from module {index}. {title}");

            await GetClipAsync(clip, course.Header, index, title, list: false);

            Utils.WriteYellowText($"Download complete");
        }

        private async Task<Course> GetCourseAsync(string courseName, bool list)
        {
            var course = await pluralsightService.GetCourseAsync(courseName);
            if (course == null)
                throw new Exception("The course was not found.");

            var hasAccess = await pluralsightService.HasCourseAccess(course.Header.Id);
            if (!hasAccess && !list)
                throw new Exception("You do not have permission to download this course");
            else if (!hasAccess && list)
                Utils.WriteRedText("Warning: You do not have permission to download this course");

            return course;
        }

        private async Task GetModuleAsync(Header course, Module module, int index, bool list)
        {
            if (module == null)
                throw new Exception("The module was not found. Check the module and Try again.");

            Utils.WriteGreenText($"\t{index}. {module.Title}", newLine: !list);
            if (list) Utils.WriteCyanText($"  --  {module.Id}");

            foreach (var clip in module.Clips)
                await GetClipAsync(clip, course, index, module.Title, list);
        }

        private async Task GetClipAsync(Clip clip, Header course, int moduleId, string moduleTitle, bool list)
        {
            if (clip == null)
                throw new Exception("The clip was not found. Check the clip and Try again.");

            Utils.WriteText($"\t\t{clip.Index}. {clip.Title}", newLine: false);
            if (list)
            {
                Utils.WriteCyanText($"  --  {clip.Id}");
                return;
            }

            var response = await pluralsightService.GetClipUrlsAsync(course.Id, clip.Id);
            if (!response.IsSuccess)
            {
                Utils.WriteRedText($"\t\t---Error retrieving clip '{clip.Title}'");
                return;
            }

            foreach (var (item, index) in response.Data.RankedOptions.WithIndex())
            {
                try
                {
                    var filePath = FileHelper.GetVideoPath(options.OutputPath, course.Title, moduleId, moduleTitle, clip);
                    if (!await downloadClient.Download(item, filePath))
                    {
                        Utils.WriteRedText($"\t\t---Invalid link: Cdn: {item.Cdn} with Url: {item.Url}");
                        continue;
                    }

                    var padding = index != 0 ? "\t\t  " : "  ";
                    Utils.WriteBlueText($"{padding}--  completed");
                    break;
                }
                catch (Exception)
                {
                    var newLine = index == 0 ? "\n" : "";

                    if (index == response.Data.RankedOptions.Count - 1)
                        Utils.WriteRedText($"\t\t  --  Download failed. To download this video run \n\t\t  '--out <Output Path> --course {course.Name} --clip {clip.Id}'");
                    else
                        Utils.WriteRedText($"{newLine}\t\t  --  Failed: Retry #{index + 1}");
                    continue;
                }
            }
        }
    }
}
