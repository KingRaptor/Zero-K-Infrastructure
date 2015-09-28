﻿using System.Collections.Generic;
using System.Text;
using System.Web.Mvc;

namespace ZeroKWeb.ForumParser
{
    public class BTagOpen: OpeningTag<BTagClose>
    {
        public override string Match { get; } = "[b]";


        public override LinkedListNode<Tag> Translate(TranslateContext context, LinkedListNode<Tag> self) {
            context.Append("<strong>");
            return self.Next;
        }

        public override Tag Create() => new BTagOpen();
    }

    public class BTagClose : ClosingTag
    {
        public override string Match { get; } = "[/b]";

        public override LinkedListNode<Tag> Translate(TranslateContext context, LinkedListNode<Tag> self)
        {
            context.Append("</strong>");
            return self.Next;
        }

        public override Tag Create()=>new BTagClose();
    }

}