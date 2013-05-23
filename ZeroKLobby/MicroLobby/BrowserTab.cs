﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace ZeroKLobby
{
    public class BrowserTab: WebBrowser, INavigatable
    {
        int navigatedIndex = 0;
        readonly List<string> navigatedPlaces = new List<string>();
        string navigatingTo = null;

        public BrowserTab() {
            
        }

        protected override void OnNavigated(WebBrowserNavigatedEventArgs e) {
            if (navigatingTo == e.Url.ToString()) {
                navigatingTo = null;
                if (navigatedIndex == navigatedPlaces.Count) navigatedPlaces.Add(e.Url.ToString());
                else navigatedPlaces[navigatedIndex] = e.Url.ToString();
                navigatedIndex++;
                Program.MainWindow.navigationControl.Path = Uri.UnescapeDataString(e.Url.ToString());
            }
            base.OnNavigated(e);
        }

        protected override void OnNavigating(WebBrowserNavigatingEventArgs e) {
            if (navigatingTo == null) navigatingTo = e.Url.ToString();
            base.OnNavigating(e);
        }

        public string PathHead { get { return "http://"; } }

        public bool TryNavigate(params string[] path) {
            var pathString = String.Join("/", path);
            if (!pathString.StartsWith(PathHead)) return false;

            pathString = Program.BrowserInterop.AddAuthToUrl(pathString);
            var browsurl = Url != null ? Url.ToString() : null;
            if (navigatingTo == pathString || browsurl == pathString) return true; // already navigating there

            Navigate(pathString);

            return true;
        }

        public bool Hilite(HiliteLevel level, params string[] path) {
            return false;
        }

        public string GetTooltip(params string[] path) {
            throw new NotImplementedException();
        }
    }
}