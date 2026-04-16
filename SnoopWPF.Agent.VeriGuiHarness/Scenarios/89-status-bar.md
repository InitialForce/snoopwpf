# Scenario: get_properties — read status bar text

Read the status message from a StatusBar after an action.

```click
{"locator": "//Button[@Name='ApplyBtn']"}
```

```wait_for_property
{"locator": "//StatusBarItem[@Name='StatusMsg']", "property": "Content", "expectedValue": "Applied", "timeoutMs": 2000}
```
