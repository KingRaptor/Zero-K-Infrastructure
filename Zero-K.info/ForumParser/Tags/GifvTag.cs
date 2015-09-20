﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Mvc;

namespace ZeroKWeb.ForumParser
{
    // embed player to display gifv (supported: gifv)
    public class GifvOpenTag: OpeningTag<GifvCloseTag>
    {
        public override string Match { get; } = "[gifv]";

        public override LinkedListNode<Tag> Translate(StringBuilder sb, LinkedListNode<Tag> self, HtmlHelper html) {
            var closing = ForumWikiParser.NextNodeOfType<GifvCloseTag>(self);

            var content = (self.Next?.Value as LiteralTag)?.Content.ToString();
            if (!string.IsNullOrEmpty(content))
            {
                var match = Regex.Match(content, @"(https?\:\/\/)((\w|-|_|\.|\/)+\.)(gifv|mp4|webm|gif)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var urlPart = match.Groups[1].Value + match.Groups[2].Value;
                    sb.AppendFormat(
                        "<div class=\"video-container\"><video preload=\"auto\" height=\"auto\" width=\"auto\" autoplay=\"autoplay\" muted=\"muted\" loop=\"loop\" ><source type=\"video/webm\" src=\"{0}webm\"><source type=\"video/mp4\" src=\"{0}mp4\">Your browser does not support the video tag. Find out if your Browser is supported at www.w3schools.com/tags/tag_video.asp</video></div>",
                        urlPart); // this looks just wrong
                }
            }

            return closing.Next;
        }

        public override Tag Create() => new GifvOpenTag();
    }

    public class GifvCloseTag: ScanningTag
    {
        public override string Match { get; } = "[/gifv]";

        public override LinkedListNode<Tag> Translate(StringBuilder sb, LinkedListNode<Tag> self, HtmlHelper html) {
            throw new ApplicationException("This isn't happening"); // should not be called
        }

        public override Tag Create() => new GifvCloseTag();
    }
}