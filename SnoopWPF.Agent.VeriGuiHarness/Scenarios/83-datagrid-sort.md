# Scenario: click → poll_changes — sort a DataGrid column

Click a column header to sort and poll for order change.

```click
{"locator": "//DataGridColumnHeader[@Name='NameColumn']"}
```

```pump_until_idle
{"timeoutMs": 1000}
```

```get_properties
{"locator": "//DataGrid[@Name='DataTable']", "properties": ["Items"]}
```
