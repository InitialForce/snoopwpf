# Scenario: execute_command — Paste into focused TextBox

Execute Paste on a TextBox.

```click
{"locator": "//TextBox[@Name='Editor']"}
```

```execute_command
{"locator": "//TextBox[@Name='Editor']", "command": "ApplicationCommands.Paste"}
```
