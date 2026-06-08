namespace Pintc;

enum Severity { Error, Warning }

record Diagnostic(Severity Severity, SourceSpan Span, string Message);
