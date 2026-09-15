using TubeRunner.Build;

try
{
    return Commands.Run(args);
}
catch (BuildFailure failure)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(failure.Message);
    Console.ResetColor();
    return 1;
}
