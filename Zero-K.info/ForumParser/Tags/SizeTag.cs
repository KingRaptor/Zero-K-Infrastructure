﻿using System.Collections.Generic;
using System.Text;
using System.Web.Mvc;

namespace ZeroKWeb.ForumParser
{
    public class SizeOpenTag: OpeningArgsTag<SizeCloseTag>
    {
        public override string Match { get; } = "[size=";
        public override char MatchTerminator { get; } = ']';

        public override LinkedListNode<Tag> Translate(StringBuilder sb, LinkedListNode<Tag> self, HtmlHelper html) {
            sb.AppendFormat("<font size=\"{0}\">", args);
            return self.Next;
        }

        public override Tag Create() => new SizeOpenTag();

        protected override bool ValidateArgs() {
            var str = args.ToString();
            return args.Length > 0 && !str.Contains("'") && !str.Contains("\"");
        }
    }

    public class SizeCloseTag: ScanningTag
    {
        public override string Match { get; } = "[/size]";

        public override LinkedListNode<Tag> Translate(StringBuilder sb, LinkedListNode<Tag> self, HtmlHelper html) {
            sb.Append("</font>");
            return self.Next;
        }

        public override Tag Create() => new SizeCloseTag();
    }
}