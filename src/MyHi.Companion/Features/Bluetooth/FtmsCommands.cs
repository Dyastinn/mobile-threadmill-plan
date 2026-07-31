// Pure command-building and response-decoding logic (FtmsCommands, FtmsOpCode,
// FtmsResultCode, ControlPointResponse, ControlPointResponseParser) lives in
// MyHi.Companion.Core.Ftms so it can be unit-tested without an Android target — see
// MyHi.Companion.Tests. This file exists at the path TASKS.md 0.7 names, and makes
// those types available throughout this project without an explicit using on every
// call site.
global using MyHi.Companion.Core.Ftms;
