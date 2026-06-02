Option Explicit

Dim fso, shell, root, port, index, dotnet, command

Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")

root = fso.GetParentFolderName(WScript.ScriptFullName)
port = "5214"

For index = 0 To WScript.Arguments.Count - 1
    If LCase(WScript.Arguments(index)) = "-port" And index + 1 < WScript.Arguments.Count Then
        port = WScript.Arguments(index + 1)
    End If
Next

dotnet = shell.ExpandEnvironmentStrings("%ProgramFiles%") & "\dotnet\dotnet.exe"
If Not fso.FileExists(dotnet) Then
    dotnet = "dotnet"
End If

command = "cmd.exe /c cd /d """ & root & """ " & _
    "&& set ASPNETCORE_ENVIRONMENT=Development " & _
    "&& """ & dotnet & """ run --project """ & root & "\Proposal\Proposal.csproj"" --urls ""http://localhost:" & port & """ " & _
    "> """ & root & "\proposal-dev.out.log"" 2> """ & root & "\proposal-dev.err.log"""

shell.Run command, 0, False
