﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Mvc;

namespace ZeroKWeb.ForumParser
{
    public class ForumWikiParser
    {
        static readonly List<Tag> nonterminalTags = new List<Tag>();
        static readonly List<TerminalTag> terminalTags = new List<TerminalTag> { new NewLineTag(), new SpaceTag(), new LiteralTag() };
        static readonly List<Tuple<Type, Type>> openClosePairs = new List<Tuple<Type, Type>>();

        static ForumWikiParser() {
            // load all classes using reflection
            foreach (var t in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (typeof(Tag).IsAssignableFrom(t) && t != typeof(LiteralTag) && !t.IsAbstract && !typeof(TerminalTag).IsAssignableFrom(t))
                {
                    var tag = (Tag)t.GetConstructor(new Type[] { }).Invoke(null);
                    nonterminalTags.Add(tag);

                    // find matching open close pairs and store them
                    if (typeof(IOpeningTag).IsAssignableFrom(t)) openClosePairs.Add(new Tuple<Type, Type>(t, ((IOpeningTag)tag).ClosingTagType));
                }
            }
        }

        List<Tag> InitNonTerminals() {
#if DEBUG
            var ret = new List<Tag>(nonterminalTags.Count);
            foreach (var nt in nonterminalTags)
            {
                var created = nt.Create();
                if (created.GetType() != nt.GetType()) throw new ApplicationException("Each parser tag must create its own clone");
                ret.Add(created);
            }
            return ret;
#else
            return nonterminalTags.Select(x => x.Create()).ToList();
#endif
        }

        public string ProcessToHtml(string input, HtmlHelper html) {
            var tags = ParseToTags(input);
            return RenderTags(tags, html);
        }

        /// <summary>
        ///     Parses input string to tag list
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        LinkedList<Tag> ParseToTags(string input) {
            var candidates = InitNonTerminals();

            var tags = new LinkedList<Tag>();

            var pos = 0;
            var scanStart = 0;

            while (pos < input.Length)
            {
                var letter = input[pos];

                foreach (var c in candidates.ToList())
                {
                    var ret = c.ScanLetter(letter);
                    if (ret == true)
                    {
                        tags.AddLast(c);
                        candidates.Clear();
                        scanStart = pos + 1;
                    } else if (ret == false) candidates.Remove(c);
                }

                if (candidates.Count == 0) // we are not matching any nonterminal tags
                {
                    if (pos - scanStart >= 0) ParseTerminals(input, scanStart, pos, tags);
                    scanStart = pos + 1;
                    candidates = InitNonTerminals();
                }
                pos++;
            }

            return tags;
        }

        /// <summary>
        ///     Renders final tags to html string builder
        /// </summary>
        /// <param name="tags"></param>
        /// <returns></returns>
        static string RenderTags(LinkedList<Tag> tags, HtmlHelper html) {
            var sb = new StringBuilder();

            tags = EliminateUnclosedTags(tags);

            var node = tags.First;
            while (node != null) node = node.Value.Translate(sb, node, html);
            return sb.ToString();
        }


        /// <summary>
        ///     Elimintes unclosed tags or unopened tags like [b] without closing [/b]
        /// </summary>
        /// <param name="input">parsed tags</param>
        /// <returns></returns>
        static LinkedList<Tag> EliminateUnclosedTags(LinkedList<Tag> input) {
            var openedTagsStack = new Stack<Tag>();
            var toDel = new List<Tag>();

            foreach (var tag in input)
            {
                var type = tag.GetType();
                if (openClosePairs.Any(y => y.Item1 == type)) openedTagsStack.Push(tag);
                else
                {
                    var closedPair = openClosePairs.FirstOrDefault(y => y.Item2 == type);
                    if (closedPair != null)
                    {
                        Tag peek;
                        if (openedTagsStack.Count == 0 || ((peek = openedTagsStack.Peek()) == null) || peek.GetType() != closedPair.Item1) toDel.Add(tag);
                        else openedTagsStack.Pop();
                    }
                }
            }

            foreach (var td in toDel) input.Remove(td); // delete extra closing tags
            while (openedTagsStack.Count > 0) input.Remove(openedTagsStack.Pop()); // delete extra opening tags

            return input;
        }

        /// <summary>
        ///     Parses terminal symbols - like string constants
        /// </summary>
        /// <param name="input">string to be prcessed</param>
        /// <param name="scanStart">start position</param>
        /// <param name="pos">end position (included)</param>
        /// <param name="tags">current tags linked list to be added to</param>
        static void ParseTerminals(string input, int scanStart, int pos, LinkedList<Tag> tags) {
            for (var i = scanStart; i <= pos; i++)
            {
                var scanChar = input[i];
                var term = terminalTags.First(x => x.ScanLetter(scanChar) == true);
                var lastTerm = tags.Last?.Value as TerminalTag;

                if (lastTerm?.GetType() == term.GetType()) lastTerm.Append(scanChar);
                else
                {
                    term = (TerminalTag)term.Create(); // create fresh instance
                    term.Append(scanChar);
                    tags.AddLast(term);
                }
            }
        }

        public static bool IsValidLink(string content) {
            return Regex.IsMatch(content, "(mailto|spring|http|https|ftp|ftps|zk)\\://[^\\\"']+$", RegexOptions.IgnoreCase);
        }

        public static LinkedListNode<Tag> NextNodeOfType<T>(LinkedListNode<Tag> node)
        {
            while (node != null && !(node.Value is T)) node = node.Next;
            return node;
        }

    }
}