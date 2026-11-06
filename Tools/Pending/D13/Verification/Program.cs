using System;
using System.Reflection;
using MapClash.Tests;
using NUnit.Framework;

internal static class Program
{
    private static int Main()
    {
        var failures = 0;
        var count = 0;
        var fixture = new DiplomacyTests();
        foreach (var method in typeof(DiplomacyTests).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var cases = method.GetCustomAttributes<TestCaseAttribute>();
            var hasCases = false;
            foreach (var testCase in cases)
            {
                hasCases = true;
                count++;
                failures += Run(fixture, method, testCase.Arguments);
            }

            if (!hasCases && method.GetCustomAttribute<TestAttribute>() != null)
            {
                count++;
                failures += Run(fixture, method, Array.Empty<object>());
            }
        }

        Console.WriteLine($"Isolated diplomacy checks: {count - failures}/{count} passed; {failures} failed.");
        return failures == 0 ? 0 : 1;
    }

    private static int Run(object fixture, MethodInfo method, object[] arguments)
    {
        try
        {
            method.Invoke(fixture, arguments);
            Console.WriteLine("PASS " + method.Name + "(" + string.Join(",", arguments) + ")");
            return 0;
        }
        catch (Exception exception)
        {
            var failure = exception is TargetInvocationException invocation ? invocation.InnerException : exception;
            Console.WriteLine("FAIL " + method.Name + ": " + failure);
            return 1;
        }
    }
}
