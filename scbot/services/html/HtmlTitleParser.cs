﻿using System;
using System.Net;
using HtmlAgilityPack;

namespace scbot.services.html
{
    public class HtmlTitleParser : IHtmlTitleParser
    {
        public string GetHtmlTitle(string url)
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(new WebClient().DownloadString(url));
                var title = doc.DocumentNode.SelectSingleNode("//title");
                return title.InnerText;
            }
            catch (Exception)
            {
                // TODO: log
                return null;
            }
        }
    }
}