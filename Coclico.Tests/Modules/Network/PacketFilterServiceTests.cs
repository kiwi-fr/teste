using System.Net;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests.Modules.Network;

public class PacketFilterServiceTests
{
    public static readonly IEnumerable<object[]> PublicTestDomains = new[]
    {
        "a.clarity.ms",
        "a.teads.tv",
        "aan.amazon.com",
        "aax.amazon-adsystem.com",
        "acdn.adnxs.com",
        "ad.doubleclick.net",
        "ad.gt",
        "ad.linkynergy.com",
        "ad.mail.ru",
        "adc3-launch.adcolony.com",
        "adcolony.com",
        "adfox.yandex.ru",
        "adfstat.yandex.ru",
        "adm.hotjar.com",
        "adnxs.com",
        "ads-api.tiktok.com",
        "ads-api.twitter.com",
        "ads-api.x.com",
        "ads-sg.tiktok.com",
        "ads.ads.oppomobile.com",
        "ads.huawei.com",
        "ads.linkedin.com",
        "ads.microsoft.com",
        "ads.pinterest.com",
        "ads.pubmatic.com",
        "ads.roku.com",
        "ads.snapchat.com",
        "ads.tiktok.com",
        "ads.tremorhub.com",
        "ads.yahoo.com",
        "ads.youtube.com",
        "ads30.adcolony.com",
        "adsdk.microsoft.com",
        "adserver-us.adtech.advertising.com",
        "adserver.unityads.unity3d.com",
        "adservice.google.com",
        "adservice.google.de",
        "adsfs.oppomobile.com",
        "adsrvr.org",
        "adtago.s3.amazonaws.com",
        "adtagos.s3.amazonaws.com",
        "adtech.yahooinc.com",
        "advice-ads.s3.amazonaws.com",
        "adx.adform.net",
        "adx.ads.oppomobile.com",
        "afs.googlesyndication.com",
        "amplifypixel.outbrain.com",
        "an.facebook.com",
        "analytics-api.samsunghealthcn.com",
        "analytics-sg.tiktok.com",
        "analytics.google.com",
        "analytics.pointdrive.linkedin.com",
        "analytics.query.yahoo.com",
        "analytics.s3.amazonaws.com",
        "analytics.tiktok.com",
        "analytics.yahoo.com",
        "analyticsengine.s3.amazonaws.com",
        "ap.lijit.com",
        "apex.go.sonobi.com",
        "api-adservices.apple.com",
        "api-eu.mixpanel.com",
        "api-in.mixpanel.com",
        "api-js.mixpanel.com",
        "api.ad.xiaomi.com",
        "api.amplitude.com",
        "api.bugsnag.com",
        "api.eu.amplitude.com",
        "api.fpjs.io",
        "api.fyber.com",
        "api.ipify.org",
        "api.luckyorange.com",
        "api.mixpanel.com",
        "api.mouseflow.com",
        "api.rlcdn.com",
        "api.segment.io",
        "api.taboola.com",
        "api.viglink.com",
        "api.vungle.com",
        "api2.amplitude.com",
        "api2.branch.io",
        "app-measurement.com",
        "app.adjust.com",
        "app.appsflyer.com",
        "app.bugsnag.com",
        "app.getsentry.com",
        "app.usercentrics.eu",
        "appmetrica.yandex.ru",
        "appusercentrics.eu",
        "as.casalemedia.com",
        "assets.adobetm.com",
        "auction.unityads.unity3d.com",
        "b.scorecardresearch.com",
        "bam.nr-data.net",
        "bas.nr-data.net",
        "bat.bing.com",
        "bdapi-ads.realmemobile.com",
        "bdapi-in-ads.realmemobile.com",
        "beap-bc.yahoo.com",
        "bidder.criteo.com",
        "biddr.criteo.com",
        "bnc.lt",
        "books-analytics-events.apple.com",
        "browser.sentry-cdn.com",
        "business-api.tiktok.com",
        "c.clarity.ms",
        "c.gumgum.com",
        "c.heapanalytics.com",
        "capi.connatix.com",
        "careers.hotjar.com",
        "cas.criteo.com",
        "casalemedia.com",
        "cch.casalemedia.com",
        "cch.doubleverify.com",
        "cck.cookiebot.org",
        "cd.connatix.com",
        "cdn-test.mouseflow.com",
        "cdn.amplitude.com",
        "cdn.cookielaw.org",
        "cdn.doubleverify.com",
        "cdn.dynamicyield.com",
        "cdn.heapanalytics.com",
        "cdn.indexexchange.com",
        "cdn.kargo.com",
        "cdn.luckyorange.com",
        "cdn.matomo.cloud",
        "cdn.mgid.com",
        "cdn.mixpanel.com",
        "cdn.mouseflow.com",
        "cdn.mxpnl.com",
        "cdn.optimizely.com",
        "cdn.permutive.com",
        "cdn.privacy-mgmt.com",
        "cdn.segment.com",
        "cdn.siftscience.com",
        "cdn.taboola.com",
        "cdn.teads.tv",
        "cdn.viglink.com",
        "ce.lijit.com",
        "ck.ads.oppomobile.com",
        "ck.pinterest.com",
        "claritytb.freshmarketer.com",
        "clevertap-prod.com",
        "click.google-analytics.com",
        "click.googleanalytics.com",
        "click.linkynergy.com",
        "click.oneplus.cn",
        "clickadu.com",
        "coinhive.com",
        "coinimp.com",
        "config.samsungads.com",
        "config.unityads.unity3d.com",
        "connect.facebook.net",
        "consent.cookiebot.com",
        "consent.trustarc.com",
        "consentcdn.cookiebot.com",
        "contextual.media.net",
        "contextweb.com",
        "control.kochava.com",
        "crypto-loot.org",
        "cs.luckyorange.net",
        "ct.pinterest.com",
        "d.adroll.com",
        "d.applovin.com",
        "d.impactradius-event.com",
        "d.reddit.com",
        "dai.google.com",
        "data.ads.oppomobile.com",
        "data.mistat.india.xiaomi.com",
        "data.mistat.rus.xiaomi.com",
        "data.mistat.xiaomi.com",
        "decide.mixpanel.com",
        "device-metrics-eu-2.amazon.com",
        "device-metrics-us-2.amazon.com",
        "device-metrics-us.amazon.com",
        "direct.adsrvr.org",
        "dis.criteo.com",
        "doubleverify.com",
        "eb2.3lift.com",
        "edge.fullstory.com",
        "eku.privacy-center.org",
        "etrackb.freshmarketer.com",
        "eu.i.posthog.com",
        "events.3alt.adcolony.com",
        "events.hotjar.io",
        "events.reddit.com",
        "events.redditmedia.com",
        "events.segmentapis.com",
        "events3alt.adcolony.com",
        "exoclick.com",
        "extmaps-api.yandex.net",
        "fastlane.rubiconproject.com",
        "firebase-settings.crashlytics.com",
        "freshmarketer.com",
        "fullstory.com",
        "fw.adsafeprotected.com",
        "fyber.com",
        "g.jwpsrv.com",
        "gemini.yahoo.com",
        "geo.yahoo.com",
        "geolocation.cookiebot.org",
        "geolocation.onetrust.com",
        "go.skimresources.com",
        "google-analytics.com",
        "googleads.g.doubleclick.net",
        "googleadservices.com",
        "googlesyndication.g.doubleclick.net",
        "googletagmanager.com",
        "graph.facebook.com",
        "grs.hicloud.com",
        "gslbeacon.lijit.com",
        "gtm.mouseflow.com",
        "gum.criteo.com",
        "hbopenbid.pubmatic.com",
        "heapanalytics.com",
        "htlb.casalemedia.com",
        "iadsdk.apple.com",
        "ib.adnxs.com",
        "id5-sync.com",
        "identify.hotjar.com",
        "idsync.rlcdn.com",
        "image.adsafeprotected.com",
        "image6.pubmatic.com",
        "images.taboola.com",
        "imasdk.googleapis.com",
        "in.hotjar.com",
        "init.supersonicads.com",
        "insight.adsrvr.org",
        "insightexpressai.com",
        "insights.hotjar.com",
        "iot-eu-logser.realme.com",
        "iot-logser.realme.com",
        "ironsource.mobi",
        "ironsrc.com",
        "ironsrc.net",
        "js-sec.indexww.com",
        "js.adsrvr.org",
        "js.drift.com",
        "juicyads.com",
        "live.chartboost.com",
        "log.byteoversea.com",
        "log.fc.yahoo.com",
        "log.outbrain.com",
        "log.pinterest.com",
        "logbak.hicloud.com",
        "logservice.hicloud.com",
        "logservice1.hicloud.com",
        "logx.optimizely.com",
        "luckyorange.com",
        "m.byteoversea.com",
        "m.doubleclick.net",
        "mads-eu.amazon.com",
        "match.adsrvr.org",
        "matomo.cloud",
        "mc.yandex.ru",
        "media.net",
        "mediavisor.doubleclick.net",
        "metrics.brightcove.com",
        "metrics.data.hicloud.com",
        "metrics.icloud.com",
        "metrics.mzstatic.com",
        "metrics2.data.hicloud.com",
        "metrika.yandex.ru",
        "mineral.io",
        "minero.cc",
        "mon.byteoversea.com",
        "mouseflow.com",
        "ms.applovin.com",
        "ms.approvin.com",
        "msadsscale.microsoft.com",
        "mssl.fwmrm.net",
        "munchkin.marketo.net",
        "ngfts.lge.com",
        "nmetrics.samsung.com",
        "notes-analytics-events.apple.com",
        "notify.bugsnag.com",
        "nr-data.net",
        "nr.taboola.com",
        "nym1-ib.adnxs.com",
        "o-collector.fullstory.com",
        "o2.mouseflow.com",
        "odb.outbrain.com",
        "offerwall.yandex.net",
        "onclickads.net",
        "onesignal.com",
        "onetag-sys.com",
        "open.oneplus.net",
        "openx.net",
        "outcome-ssp.supersonicads.com",
        "pagead2.googleadservices.com",
        "pagead2.googlesyndication.com",
        "partnerads.ysm.yahoo.com",
        "ping.pinterest.com",
        "pippio.com",
        "pixel.adsafeprotected.com",
        "pixel.facebook.com",
        "pixel.mathtag.com",
        "pixel.quantserve.com",
        "pixel.quora.com",
        "pixel.rubiconproject.com",
        "plausible.io",
        "popads.net",
        "popcash.net",
        "prebid.adnxs.com",
        "prod.uidapi.com",
        "pubads.g.doubleclick.net",
        "px.ads.linkedin.com",
        "quantserve.com",
        "r.applovin.com",
        "r.youtube.com",
        "realtime.luckyorange.com",
        "redirect.viglink.com",
        "redirector.skimresources.com",
        "region1.google-analytics.com",
        "regionconfig.amplitude.com",
        "rs.fullstory.com",
        "rt.applovin.com",
        "rtb.openx.net",
        "s.adroll.com",
        "s.clarity.ms",
        "s.innovid.com",
        "s.pining.com",
        "s.skimresources.com",
        "s.youtube.com",
        "s1.adform.net",
        "samsung-com.112.2o7.net",
        "samsung-com.t112.2o7.net",
        "samsungads.com",
        "sb.scorecardresearch.com",
        "sc-analytics.appspot.com",
        "sc-static.net",
        "scorecardresearch.com",
        "script.crazyegg.com",
        "script.hotjar.com",
        "scripts.clarity.ms",
        "sdk.privacy-center.org",
        "sdkconfig.ad.intl.xiaomi.com",
        "sdkconfig.ad.xiaomi.com",
        "secure.adnxs.com",
        "secure.quantserve.com",
        "securepubads.g.doubleclick.net",
        "server.adform.net",
        "servex.mgid.com",
        "servicer.mgid.com",
        "sessions.bugsnag.com",
        "settings.luckyorange.net",
        "sin3-ib.adnxs.com",
        "smartclip.com",
        "smartclip.net",
        "smetrics.samsung.com",
        "snap.licdn.com",
        "snap.linkedin.com",
        "sq.apple.com",
        "sr-client-cfg.amplitude.com",
        "sr-client-cfg.eu.amplitude.com",
        "ssl.google-analytics.com",
        "sslwidget.criteo.com",
        "sstats.adobe.com",
        "statdynamic.com",
        "static.ads-twitter.com",
        "static.adsafeprotected.com",
        "static.criteo.net",
        "static.doubleclick.net",
        "static.hotjar.com",
        "static.media.net",
        "stats.g.doubleclick.net",
        "stats.wp.com",
        "supersonicads.com",
        "surveys.hotjar.com",
        "sync.kargo.com",
        "sync.mathtag.com",
        "t.skimresources.com",
        "tapad.com",
        "tlx.3lift.com",
        "tools.mouseflow.com",
        "top-fwz1.mail.ru",
        "tpc.googlesyndication.com",
        "tr.facebook.com",
        "tr.snapchat.com",
        "track.adform.net",
        "track.hubspot.com",
        "track.linkynergy.com",
        "trackcmp.net",
        "tracking.crazyegg.com",
        "tracking.miui.com",
        "tracking.rus.miui.com",
        "traffic.outbrain.com",
        "trafficjunky.net",
        "trc.taboola.com",
        "tremorhub.com",
        "trk.pinterest.com",
        "u.openx.net",
        "udc.yahoo.com",
        "udcm.yahoo.com",
        "upload.luckyorange.net",
        "us-ads.openx.net",
        "us.i.posthog.com",
        "us.info.lgsmartad.com",
        "vap.lijit.com",
        "vars.hotjar.com",
        "vid.connatix.com",
        "vidstat.taboola.com",
        "vortex.data.microsoft.com",
        "w1.luckyorange.com",
        "wd.adcolony.com",
        "weather-analytics-events.apple.com",
        "webview.unityads.unity3d.com",
        "widget.intercom.io",
        "widgets.outbrain.com",
        "www.androezrs.net",
        "www.anrdoezrs.net",
        "www.awin1.com",
        "www.clarity.ms",
        "www.coinimp.com",
        "www.dpbolvw.net",
        "www.google-analytics.com",
        "www.googletagmanager.com",
        "www.tkqlhce.com",
        "wzrkt.com",
        "xp.apple.com",
        "zenaps.com"
    }.Distinct().Select(d => new object[] { d });

    [Theory]
    [MemberData(nameof(PublicTestDomains))]
    public void ClassifyBlocksEveryPublicTestDomain(string host)
    {
        bool covered = TrackerDomainCatalog.Domains.Any(d =>
            host == d || host.EndsWith("." + d, StringComparison.Ordinal));
        Assert.True(covered, "Non couvert par le catalogue : " + host);

        var (v, _) = PacketFilterService.Classify(PacketParser.ProtocolTcp, IPAddress.Parse("203.0.113.7"), 443, host,
            Array.Empty<IPAddress>(), TrackerDomainCatalog.Domains);
        Assert.Equal(PacketVerdict.Block, v);
    }

    [Theory]
    [InlineData("www.google-analytics.com")]
    [InlineData("stats.g.doubleclick.net")]
    [InlineData("ad.doubleclick.net")]
    [InlineData("adservice.google.com")]
    [InlineData("static.criteo.net")]
    [InlineData("bat.bing.com")]
    [InlineData("iadsdk.apple.com")]
    [InlineData("app-measurement.com")]
    [InlineData("doubleclick.net")]
    public void ClassifyBlocksAllPublicTestDomainsBySni(string sni)
    {
        var (v, _) = PacketFilterService.Classify(PacketParser.ProtocolTcp, IPAddress.Parse("203.0.113.7"), 443, sni,
            Array.Empty<IPAddress>(), TrackerDomainCatalog.Domains);
        Assert.Equal(PacketVerdict.Block, v);
    }

    [Fact]
    public void ClassifyAllowsKnownPortAndPrivateIp()
    {
        var (v, _) = PacketFilterService.Classify(PacketParser.ProtocolTcp, IPAddress.Parse("192.168.1.10"), 443, null,
            Array.Empty<IPAddress>(), TrackerDomainCatalog.Domains);
        Assert.Equal(PacketVerdict.Allow, v);
    }

    [Fact]
    public void ClassifyStrictBlocksUnknownPortToPublicIp()
    {
        var (v, _) = PacketFilterService.Classify(PacketParser.ProtocolTcp, IPAddress.Parse("203.0.113.7"), 61234, null,
            Array.Empty<IPAddress>(), TrackerDomainCatalog.Domains, strict: true);
        Assert.Equal(PacketVerdict.Block, v);
    }

    [Fact]
    public void ClassifyNonStrictInspectsUnknownPortToPublicIp()
    {
        var (v, _) = PacketFilterService.Classify(PacketParser.ProtocolTcp, IPAddress.Parse("203.0.113.7"), 61234, null,
            Array.Empty<IPAddress>(), TrackerDomainCatalog.Domains, strict: false);
        Assert.Equal(PacketVerdict.Inspect, v);
    }

    [Fact]
    public void ClassifyBlocksKnownTrackerIp()
    {
        var trackers = new[] { IPAddress.Parse("203.0.113.7") };
        var (v, _) = PacketFilterService.Classify(PacketParser.ProtocolTcp, IPAddress.Parse("203.0.113.7"), 443, null,
            trackers, TrackerDomainCatalog.Domains);
        Assert.Equal(PacketVerdict.Block, v);
    }

    [Fact]
    public void ClassifyAllowsIcmp()
    {
        var (v, _) = PacketFilterService.Classify(PacketParser.ProtocolIcmp, IPAddress.Parse("203.0.113.7"), 0, null,
            Array.Empty<IPAddress>(), TrackerDomainCatalog.Domains);
        Assert.Equal(PacketVerdict.Allow, v);
    }

    [Fact]
    public void ClassifyHttpRequestBlocksTrackerHost()
    {
        var (v, _) = PacketFilterService.ClassifyHttpRequest("ads.pubmatic.com", "/track", TrackerDomainCatalog.Domains);
        Assert.Equal(PacketVerdict.Block, v);
    }

    [Fact]
    public void ClassifyHttpRequestBlocksAdPath()
    {
        var (v, _) = PacketFilterService.ClassifyHttpRequest("example.com", "/ads/banner.js", TrackerDomainCatalog.Domains);
        Assert.Equal(PacketVerdict.Block, v);
    }

    [Fact]
    public void ClassifyHttpRequestAllowsStandard()
    {
        var (v, _) = PacketFilterService.ClassifyHttpRequest("example.com", "/index.html", TrackerDomainCatalog.Domains);
        Assert.Equal(PacketVerdict.Allow, v);
    }

    [Fact]
    public void TryParseIPv4HeaderParsesTcpPacket()
    {
        byte[] pkt = new byte[40];
        pkt[0] = 0x45; pkt[9] = PacketParser.ProtocolTcp;
        pkt[12] = 10; pkt[13] = 0; pkt[14] = 0; pkt[15] = 1;
        pkt[16] = 203; pkt[17] = 0; pkt[18] = 113; pkt[19] = 7;
        pkt[20] = 0x30; pkt[21] = 0x39; pkt[22] = 0x01; pkt[23] = 0xBB;
        Assert.True(PacketParser.TryParseIPv4Header(pkt, out int ihl, out byte proto, out IPAddress src, out IPAddress dst));
        Assert.Equal(20, ihl);
        Assert.Equal(PacketParser.ProtocolTcp, proto);
        Assert.Equal("10.0.0.1", src.ToString());
        Assert.Equal("203.0.113.7", dst.ToString());
        PacketParser.TryParsePorts(pkt, ihl, proto, out int sp, out int dp);
        Assert.Equal(12345, sp);
        Assert.Equal(443, dp);
    }

    [Fact]
    public void TryParseDnsQueryNameParsesUdpQuery()
    {
        byte[] dns = new byte[12 + 1 + 3 + 1 + 7 + 1 + 3 + 1 + 4];
        dns[5] = 1;
        int pos = 12;
        foreach (var label in new[] { "ads", "example", "com" })
        {
            dns[pos++] = (byte)label.Length;
            foreach (char c in label) dns[pos++] = (byte)c;
        }
        dns[pos] = 0;
        Assert.True(PacketParser.TryParseDnsQueryName(dns, false, out string qname));
        Assert.Equal("ads.example.com", qname);
    }

    [Fact]
    public void GetDnsRcodeReturnsNxdomain()
    {
        byte[] dns = new byte[12];
        dns[3] = 0x83;
        Assert.Equal(3, PacketParser.GetDnsRcode(dns, false));
    }

    [Fact]
    public void ParseDnsAnswersReturnsARecords()
    {
        byte[] dns = new byte[12 + 7 + 4 + 16];
        dns[5] = 1; dns[7] = 1;
        int pos = 12;
        dns[pos++] = 1; dns[pos++] = (byte)'x';
        dns[pos++] = 3; dns[pos++] = (byte)'c'; dns[pos++] = (byte)'o'; dns[pos++] = (byte)'m';
        dns[pos++] = 0;
        pos += 4;
        dns[pos++] = 0xC0; dns[pos++] = 0x0C;
        dns[pos++] = 0; dns[pos++] = 1; dns[pos++] = 0; dns[pos++] = 1;
        dns[pos++] = 0; dns[pos++] = 0; dns[pos++] = 0; dns[pos++] = 60;
        dns[pos++] = 0; dns[pos++] = 4;
        dns[pos++] = 93; dns[pos++] = 184; dns[pos++] = 216; dns[pos++] = 34;
        var answers = PacketParser.ParseDnsAnswers(dns, false);
        Assert.Single(answers);
        Assert.Equal("93.184.216.34", answers[0].ToString());
    }

    [Fact]
    public void TryExtractHttpRequestParsesHostAndPath()
    {
        string req = "GET /ads/banner HTTP/1.1\r\nHost: Ads.Example.com\r\n\r\n";
        byte[] payload = System.Text.Encoding.ASCII.GetBytes(req);
        Assert.True(PacketParser.TryExtractHttpRequest(payload, out string host, out string path));
        Assert.Equal("ads.example.com", host);
        Assert.Equal("/ads/banner", path);
    }

    [Fact]
    public void ContainsAdTokenDetectsAsciiAdPayload()
    {
        byte[] p = System.Text.Encoding.ASCII.GetBytes("GET /doubleclick/ad.js HTTP/1.1");
        Assert.True(PacketParser.LooksLikeAscii(p));
        Assert.True(PacketParser.ContainsAdToken(p));
    }

    [Fact]
    public void IsPrivateIpClassifiesRanges()
    {
        Assert.True(PacketParser.IsPrivateIp(IPAddress.Parse("10.1.2.3")));
        Assert.True(PacketParser.IsPrivateIp(IPAddress.Parse("192.168.0.5")));
        Assert.True(PacketParser.IsPrivateIp(IPAddress.Parse("172.16.9.9")));
        Assert.False(PacketParser.IsPrivateIp(IPAddress.Parse("203.0.113.7")));
    }
}
