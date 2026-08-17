using System;
using System.Collections.Generic;
using System.Linq;

namespace ConfigExporter.Tests;

/// <summary>极简断言，供无第三方测试框架的离线环境使用。</summary>
public static class Check
{
    public static void True(bool condition, string message = "断言失败")
    {
        if (!condition)
            throw new Exception(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"期望 [{expected}]，实际 [{actual}]");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new Exception($"期望 [{string.Join(",", expected)}]，实际 [{string.Join(",", actual)}]");
    }

    public static void Contains(string haystack, string needle)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
            throw new Exception($"未找到子串 [{needle}]");
    }

    public static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new Exception($"期望抛出 {typeof(T).Name}，实际 {ex.GetType().Name}: {ex.Message}");
        }

        throw new Exception($"期望抛出 {typeof(T).Name}，但没有抛出");
    }
}

/// <summary>极简测试运行器：逐个执行，输出 PASS/FAIL，全绿返回 0。</summary>
public static class TestRunner
{
    public static int Run(params (string name, Action body)[] tests)
    {
        var pass = 0;
        var fail = 0;
        foreach (var (name, body) in tests)
        {
            try
            {
                body();
                Console.WriteLine("PASS  " + name);
                pass++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL  " + name);
                Console.WriteLine("      " + ex.Message.Replace("\n", "\n      "));
                fail++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{pass} passed, {fail} failed");
        return fail == 0 ? 0 : 1;
    }
}
