﻿using DotNetWikiBot;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ZkData;

namespace Fixer
{
    /// <summary>Ports Zero-K wiki pages to the new MediaWiki.</summary>
    public static class WikiPortingMW
    {
        public const string WIKI_URL = "http://zero-k.info";
        static Site newWiki;

        public static string BBCodeToMediaWiki(string text)
        {
            Regex regex;

            //bullet points
            regex = new Regex("\\n \\* ");
            text = regex.Replace(text, "\n* ");

            // replace bold
            regex = new Regex("\\[b\\](.*?)\\[/b\\]");
            text = regex.Replace(text, "'''$1'''");
            regex = new Regex("\\*(.*?)\\*");
            text = regex.Replace(text, "'''$1'''");

            //replace italics
            regex = new Regex("\\[i\\](.*?)\\[/i\\]");
            text = regex.Replace(text, "''$1''");
            //regex = new Regex("_(.*?)_"); // trying to remove markdown italics tends to break on filenames
            //text = regex.Replace(text, "''$1''");

            // URLs
            regex = new Regex("\\[url=(.*?)\\](.*?)\\[/url\\]");
            text = regex.Replace(text, "[$1 $2]");

            // remove [img] tags
            regex = new Regex("\\[\\/?img\\]");
            text = regex.Replace(text, "");

            // remove wiki:toc tag
            regex = new Regex("<wiki:toc .*?/>");
            text = regex.Replace(text, "");

            // code blocks
            regex = new Regex("\\{\\{\\{(.*?)\\}\\}\\}", RegexOptions.Singleline);
            text = regex.Replace(text, "<code>$1</code>");

            // Manual link
            regex = new Regex("\\[Manual Back to Manual\\]");
            text = regex.Replace(text, "[[Manual|Back to Manual]]");

            Console.WriteLine(text);
            return text;
        }

        public static void ConvertPage(string pageName, string newName, bool overwrite = false)
        {
            var db = new ZkDataContext();
            ForumThread thread = db.ForumThreads.FirstOrDefault(x=> x.WikiKey == pageName);
            if (thread == null)
            {
                Console.WriteLine("No ZK wiki page with name {0} found", pageName);
                return;
            }

            string text = thread.ForumPosts.First().Text;
            text = BBCodeToMediaWiki(text);

            Page page = new Page(newWiki, newName);
            page.Load();

            bool update = false;
            if (!page.IsEmpty())
            {
                if (!overwrite)
                {
                    Console.WriteLine("Page already exists, exiting");
                    return;
                }
                else update = true;
            }
            if (newName.StartsWith("Mission Editor", true, System.Globalization.CultureInfo.CurrentCulture))
                page.AddToCategory("Mission Editor");
            page.Save(text, update ? "" : "Ported from ZK wiki by DotNetWikiBot", update);
        }

        public static void ReformatPage(string pageName)
        {
            Page page = new Page(newWiki, pageName);
            page.Load();
            string text = BBCodeToMediaWiki(page.text);
            if (pageName.StartsWith("Mission Editor", true, System.Globalization.CultureInfo.CurrentCulture))
                page.AddToCategory("Mission Editor");
            page.Save(text, "Cleanup by DotNetWikiBot", false);
        }

        public static void AddNavbox(string pageName, string template)
        {
            Page page = new Page(newWiki, pageName);
            page.Load();
            AddNavbox(page, template);
        }

        public static void AddNavbox(Page page, string template)
        {
            string text = page.text;
            if (page.GetTemplates(false, false).Contains(template))
            {
                Console.WriteLine("Page {0} already has template {1}", page.title, template);
                return;
            }
            page.AddTemplate("{{" + template + "}}");
            page.Save(text, "Infobox added by DotNetWikiBot", true);
        }

        public static void DoStuff()
        {
            // !! MAKE SURE YOU REMOVE THE PASSWORD BEFORE COMMITTING !!
            string username = "KingRaptorBot";
            string password = "correct horse battery staple";

            newWiki = new Site(WIKI_URL, username, password);
            string[,] toPort = 
            {
                //{"MissionEditorCompatibility", "Mission Editor game compatibility"},
                //{"MissionEditorStartPage", "Mission Editor"},
                //{"MissionEditorWINE", "Mission Editor in WINE"},
                //{"FactoryOrdersTutorial", "Mission Editor Factory Orders Tutorial"},
                //{"MissionEditorTutorial", "Mission Editor Tutorial"},
                //{"MissionEditorCutsceneTutorial", "Mission Editor Cutscenes Tutorial"}
            };
            for (int i=0; i<toPort.GetLength(0); i++)
            {
                ConvertPage(toPort[i, 0], toPort[i, 1], true);
            }

            string[] toReformat =
            {
                //"Mission Editor Cutscenes Tutorial"
            };
            for (int i = 0; i < toReformat.GetLength(0); i++)
            {
                ReformatPage(toReformat[i]);
            }
        }
    }
}
