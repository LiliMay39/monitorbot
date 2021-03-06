﻿using System.Diagnostics;
using monitorbot.core.utils;
using monitorbot.jira.services;
using NUnit.Framework;

namespace monitorbot.jira.tests
{
    class JiraIntegrationTests
    {
        [Test]
        public void CanFetchJiraIssueFromId()
        {
            var jira = new JiraApi();
            var bug = jira.FromId("SDC-1604").Result;
            Assert.AreEqual("Projects occasionally blow up when loaded against dbs with schema differences", bug.Title);
            Assert.AreEqual("Open", bug.Status);
            Assert.AreEqual(1, bug.CommentCount);
            StringAssert.Contains("User has a source DB", bug.Description);
        }

        [Test]
        public void CanCacheJiraApi()
        {
            var cached = new CachedJiraApi(new Time(), new JiraApi());
            var stopwatch = new Stopwatch();

            stopwatch.Start();
            var bug = cached.FromId("SDC-1604").Result;
            var uncachedTime = stopwatch.Elapsed;

            stopwatch.Reset();
            bug = cached.FromId("SDC-1604").Result;
            var cachedTime = stopwatch.Elapsed;

            Assert.Greater(uncachedTime.TotalMilliseconds, 10);
            Assert.Less(cachedTime.TotalMilliseconds, 10);
        }

        [Test]
        public void ReturnsNullOnError()
        {
            var jira = new JiraApi();
            Assert.Null(jira.FromId("UTF-8").Result);
        }
    }
}
