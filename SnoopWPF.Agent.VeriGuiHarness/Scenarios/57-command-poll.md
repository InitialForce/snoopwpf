# Scenario: execute_command → poll_changes — command triggers state update

Execute Undo and poll for text reversion.

```execute_command
{"locator": "//Window[1]", "command": "ApplicationCommands.Undo"}
```

```poll_changes
{"locator": "//TextBox[@Name='Editor']", "properties": ["Text"], "intervalMs": 100, "maxPollCount": 5}
```
