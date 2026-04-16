# Scenario: DataGrid row selection — click row and inspect

Click a DataGrid row and verify SelectedItem is populated.

```click
{"locator": "//DataGrid[@Name='DataTable']//DataGridRow[1]"}
```

```get_properties
{"locator": "//DataGrid[@Name='DataTable']", "properties": ["SelectedItem","SelectedIndex"]}
```
