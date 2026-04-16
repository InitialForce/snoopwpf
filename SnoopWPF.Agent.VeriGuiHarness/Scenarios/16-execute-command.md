# Scenario: execute_command — invoke a RoutedCommand

Execute the ApplicationCommands.Save command on the main window.

```execute_command
{"locator": "//Window[1]", "command": "ApplicationCommands.Save"}
```

```get_properties
{"locator": "//Window[1]", "properties": ["Title"]}
```
