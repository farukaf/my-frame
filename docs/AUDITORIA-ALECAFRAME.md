# AlecaFrame compatibility audit

AlecaFrame remains an optional migration source, not a runtime requirement. Its local
inventory and catalog files may be read only by the legacy import path. The application
does not modify AlecaFrame files, reuse its market token, or expose its raw data through
MCP.

The SQLite platform and Overwolf collector are the target data path. Real collector
validation remains open; see [the delivery checklist](../todo.md).
