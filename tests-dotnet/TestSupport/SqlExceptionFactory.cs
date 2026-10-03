using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.TestSupport;

/// <summary>
/// Builds a real <see cref="SqlException"/> without a SQL Server. SqlClient keeps the
/// constructors internal, so this goes through reflection; it picks the SqlError
/// constructor by its leading parameters so a minor SqlClient upgrade that adds a trailing
/// parameter does not break it. Linked into the test assemblies that need it.
/// </summary>
internal static class SqlExceptionFactory
{
    public sealed record Error(int Number, string Message, byte State = 1, byte Class = 16, string Procedure = "", int Line = 1);

    public static SqlException Create(params Error[] errors)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var collection = (SqlErrorCollection)typeof(SqlErrorCollection).GetConstructor(flags, Type.EmptyTypes)!.Invoke(null);
        var add = typeof(SqlErrorCollection).GetMethod("Add", flags, [typeof(SqlError)])!;
        var leading = new[] { typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int) };
        var constructor = typeof(SqlError).GetConstructors(flags)
            .Where(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length >= leading.Length
                    && parameters.Take(leading.Length).Select(parameter => parameter.ParameterType).SequenceEqual(leading);
            })
            .OrderBy(candidate => candidate.GetParameters().Length)
            .First();
        foreach (var error in errors)
        {
            var parameters = constructor.GetParameters();
            var arguments = new object?[parameters.Length];
            object?[] values = [error.Number, error.State, error.Class, "server", error.Message, error.Procedure, error.Line];
            Array.Copy(values, arguments, values.Length);
            for (var index = values.Length; index < parameters.Length; index++)
                arguments[index] = parameters[index].ParameterType.IsValueType
                    ? Activator.CreateInstance(parameters[index].ParameterType)
                    : null;
            add.Invoke(collection, [constructor.Invoke(arguments)]);
        }
        var create = typeof(SqlException).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .First(method => method.Name == "CreateException"
                && method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual([typeof(SqlErrorCollection), typeof(string)]));
        return (SqlException)create.Invoke(null, [collection, "17.0.0"])!;
    }
}
