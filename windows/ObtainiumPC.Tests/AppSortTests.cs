using System;
using System.Collections.Generic;
using System.Linq;
using ObtainiumPC.Models;
using ObtainiumPC.Services.Store;
using Xunit;

namespace ObtainiumPC.Tests
{
    // lib/components/app_list_tile.dart AppListBuilder.sort
    public class AppSortTests
    {
        private static TrackedApp Mk(string name, string author, long? releaseDateUs = null) => new()
        {
            Id = name,
            Url = "https://example.com/" + name,
            Name = name,
            Author = author,
            ReleaseDateUs = releaseDateUs,
        };

        private static List<string> Names(List<TrackedApp> l) => l.Select(a => a.FinalName).ToList();

        [Fact]
        public void Added_Ascending_KeepsOrder_Descending_Reverses()
        {
            var apps = new List<TrackedApp> { Mk("c", "z"), Mk("a", "y"), Mk("b", "x") };
            Assert.Equal(new[] { "c", "a", "b" }, Names(AppSort.Sort(apps, 0, 0)));
            Assert.Equal(new[] { "b", "a", "c" }, Names(AppSort.Sort(apps, 0, 1)));
        }

        [Fact]
        public void NameAuthor_Asc_And_Desc()
        {
            var apps = new List<TrackedApp> { Mk("Beta", "a"), Mk("alpha", "z"), Mk("Gamma", "b") };
            // keys are lowercased name+author
            Assert.Equal(new[] { "alpha", "Beta", "Gamma" }, Names(AppSort.Sort(apps, 1, 0)));
            Assert.Equal(new[] { "Gamma", "Beta", "alpha" }, Names(AppSort.Sort(apps, 1, 1)));
        }

        [Fact]
        public void AuthorName_KeysOnAuthorFirst()
        {
            var apps = new List<TrackedApp> { Mk("aaa", "zz"), Mk("zzz", "aa") };
            Assert.Equal(new[] { "zzz", "aaa" }, Names(AppSort.Sort(apps, 2, 0)));
        }

        [Fact]
        public void ReleaseDate_NullsLast_AscAndDesc()
        {
            long old = 1000000000000L, recent = 2000000000000L;
            var apps = new List<TrackedApp> { Mk("noDate", "a"), Mk("old", "a", old), Mk("new", "a", recent) };
            Assert.Equal(new[] { "old", "new", "noDate" }, Names(AppSort.Sort(apps, 3, 0)));
            Assert.Equal(new[] { "new", "old", "noDate" }, Names(AppSort.Sort(apps, 3, 1)));
        }
    }
}
