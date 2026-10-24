using RebootWhy.Core;
using RebootWhy.Core.Sources;

namespace RebootWhy.Core.Tests;

public class EventXmlParserTests
{
    [Fact]
    public void ParsesShutdownRequestWithNamedData()
    {
        const string xml = """
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <System>
                <Provider Name="User32" Guid="{b0aa8734-56f7-41cc-b2f4-de228e98b946}" EventSourceName="User32" />
                <EventID Qualifiers="32768">1074</EventID>
                <Level>4</Level>
                <TimeCreated SystemTime="2026-10-24T01:30:08.1234567Z" />
                <EventRecordID>12345</EventRecordID>
                <Channel>System</Channel>
              </System>
              <EventData>
                <Data Name="param1">C:\WINDOWS\uus\AMD64\MoUsoCoreWorker.exe (ROVERNOTE3)</Data>
                <Data Name="param4">0x80020010</Data>
                <Data Name="param5">Neustart</Data>
                <Data Name="param6"></Data>
              </EventData>
            </Event>
            """;

        var e = EventXmlParser.Parse(xml);

        Assert.True(e.Is(Providers.User32, EventCatalog.ShutdownRequested));
        Assert.Equal(4, e.Level);
        Assert.Equal(12345, e.RecordId);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 1, 30, 8, TimeSpan.Zero).AddTicks(1234567), e.TimeUtc);
        Assert.Equal(0x80020010, e.GetInt64("PARAM4"));
        Assert.Equal("Neustart", e.Get("param5"));
        Assert.Equal(string.Empty, e.Get("param6"));
    }

    [Fact]
    public void NamesUnnamedClassicDataByPosition()
    {
        const string xml = """
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <System>
                <Provider Name="EventLog" />
                <EventID Qualifiers="32768">6008</EventID>
                <Level>2</Level>
                <TimeCreated SystemTime="2026-09-02T11:39:38.000Z" />
                <EventRecordID>7</EventRecordID>
              </System>
              <EventData>
                <Data>11:39:00</Data>
                <Data>02.09.2026</Data>
              </EventData>
            </Event>
            """;

        var e = EventXmlParser.Parse(xml);

        Assert.Equal(6008, e.Id);
        Assert.Equal("11:39:00", e.Get("Data0"));
        Assert.Equal("02.09.2026", e.Get("Data1"));
    }

    [Fact]
    public void ReadsUserDataLeaves()
    {
        const string xml = """
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <System>
                <Provider Name="Microsoft-Windows-Eventlog" />
                <EventID>104</EventID>
                <Level>4</Level>
                <TimeCreated SystemTime="2026-09-01T10:00:00Z" />
                <EventRecordID>1</EventRecordID>
              </System>
              <UserData>
                <LogFileCleared xmlns="http://manifests.microsoft.com/win/2004/08/windows/eventlog">
                  <SubjectUserName>alice</SubjectUserName>
                  <Channel>System</Channel>
                </LogFileCleared>
              </UserData>
            </Event>
            """;

        var e = EventXmlParser.Parse(xml);

        Assert.True(e.Is(Providers.EventLogChannel, EventCatalog.LogCleared));
        Assert.Equal("alice", e.Get("SubjectUserName"));
    }
}
