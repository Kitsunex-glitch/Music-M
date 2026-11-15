#nullable enable

using System;
using System.Collections.Generic;

namespace VK_UI3.Controls.BetterLyric;

/// <summary>
/// Результат поиска текущей строки.
/// </summary>
/// <param name="Index">Индекс строки, к которой нужно привязать полотно.</param>
/// <param name="IsActive">
/// true — строка реально звучит прямо сейчас (её интервал накрывает текущее время).
/// false — время попало в разрыв: индекс указывает на СЛЕДУЮЩУЮ строку,
/// полотно к ней едет, но подсветку/масштаб включать нельзя.
/// </param>
internal readonly record struct LyricsLineHit(int Index, bool IsActive);

internal partial class LyricsSynchronizer
{
    private int _lastFoundIndex = 0;

    public void Reset()
    {
        _lastFoundIndex = 0;
    }

    public int GetCurrentLineIndex(double currentTimeMs, IList<RenderLyricsLine>? lines)
        => GetCurrentLine(currentTimeMs, lines).Index;

    /// <summary>
    /// Ищет строку для текущего времени.
    /// <para>
    /// Если время попало в разрыв между строками (интро, пауза, инструментал),
    /// возвращается СЛЕДУЮЩАЯ строка с <see cref="LyricsLineHit.IsActive"/> = false:
    /// полотно плавно уезжает к ней, но она ещё не считается звучащей.
    /// </para>
    /// </summary>
    public LyricsLineHit GetCurrentLine(double currentTimeMs, IList<RenderLyricsLine>? lines)
    {
        if (lines == null || lines.Count == 0) return new LyricsLineHit(0, false);

        // Быстрый путь: прошлая найденная строка всё ещё накрывает текущее время.
        if (_lastFoundIndex >= 0 && _lastFoundIndex < lines.Count)
        {
            var lastLine = lines[_lastFoundIndex];
            if (lastLine.LaneIndex == 0 && IsTimeInLine(currentTimeMs, lines, _lastFoundIndex))
            {
                return new LyricsLineHit(_lastFoundIndex, true);
            }
        }

        int bestCandidateIndex = -1;
        int bestCandidateLane = int.MaxValue;

        // Первая строка, которая начнётся ПОСЛЕ текущего времени — цель для разрыва.
        int nextUpcomingIndex = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            if (IsTimeInLine(currentTimeMs, lines, i))
            {
                var currentLine = lines[i];
                int currentLane = currentLine.LaneIndex;

                if (currentLane == 0)
                {
                    _lastFoundIndex = i;
                    return new LyricsLineHit(i, true);
                }

                if (currentLane < bestCandidateLane)
                {
                    bestCandidateIndex = i;
                    bestCandidateLane = currentLane;
                }
            }
            else if (lines[i].StartMs > currentTimeMs)
            {
                // Строки упорядочены по времени — дальше искать бессмысленно.
                if (nextUpcomingIndex < 0) nextUpcomingIndex = i;
                break;
            }
        }

        if (bestCandidateIndex != -1)
        {
            _lastFoundIndex = bestCandidateIndex;
            return new LyricsLineHit(bestCandidateIndex, true);
        }

        // Разрыв: подсветки нет, но полотно ведём к следующей строке.
        if (nextUpcomingIndex >= 0)
        {
            _lastFoundIndex = nextUpcomingIndex;
            return new LyricsLineHit(nextUpcomingIndex, false);
        }

        // Время за пределами текста (аутро) — держим последнюю строку.
        int lastIndex = lines.Count - 1;
        _lastFoundIndex = lastIndex;
        return new LyricsLineHit(lastIndex, false);
    }

    private static bool IsTimeInLine(double time, IList<RenderLyricsLine> lines, int index)
    {
        if (index < 0 || index >= lines.Count) return false;
        var line = lines[index];
        var nextLine = (index + 1 < lines.Count) ? lines[index + 1] : null;
        if (time < line.StartMs) return false;
        if (nextLine != null && time >= nextLine.StartMs) return false;
        return true;
    }
}
