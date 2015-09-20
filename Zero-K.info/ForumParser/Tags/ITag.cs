﻿using System.Collections.Generic;
using System.Text;
using System.Web.Mvc;

namespace ZeroKWeb.ForumParser
{
    public class ITagOpen: OpeningTag<ITagClose>
    {
        public override string Match { get; } = "[i]";


        public override LinkedListNode<Tag> Translate(StringBuilder sb, LinkedListNode<Tag> self, HtmlHelper html) {
            sb.Append("<em>");
            return self.Next;
        }

        public override Tag Create() {
            return new ITagOpen();
        }
    }

    public class ITagClose: ScanningTag
    {
        public override string Match { get; } = "[/i]";

        public override LinkedListNode<Tag> Translate(StringBuilder sb, LinkedListNode<Tag> self, HtmlHelper html) {
            sb.Append("</em>");
            return self.Next;
        }

        public override Tag Create() {
            return new ITagClose();
        }
    }
}

