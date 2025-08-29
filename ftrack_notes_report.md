# Adding Notes to Entities in ftrack: Comprehensive Report

## Executive Summary

This report provides detailed information on how to add notes to entities in ftrack using both the web interface and programmatic approaches through the ftrack API. Notes in ftrack are versatile communication tools that can be attached to almost any entity (tasks, shots, assets, projects, etc.) and support features like replies, attachments, labels, categories, and recipients.

## Table of Contents

1. [Overview of Notes in ftrack](#overview)
2. [Creating Notes via Web Interface](#web-interface)
3. [Programmatic Note Creation with Python API](#python-api)
4. [Note Features and Capabilities](#features)
5. [Code Examples](#examples)
6. [Best Practices](#best-practices)
7. [Resources and Documentation Links](#resources)

## Overview of Notes in ftrack {#overview}

Notes in ftrack serve as a collaborative communication system that allows team members to:

- **Communicate** about specific entities (tasks, shots, assets, etc.)
- **Leave feedback** and instructions
- **Track conversations** with threaded replies
- **Attach files** for additional context
- **Notify specific users** or groups
- **Categorize communications** with labels and categories

**Source:** [ftrack Developer Documentation - Using Notes](https://developer.ftrack.com/api-clients/examples/note/)

## Creating Notes via Web Interface {#web-interface}

### Basic Note Creation

1. **Navigate to any entity** (task, shot, asset, project, etc.)
2. **Locate the Notes panel** (usually in the entity details view)
3. **Click "Add Note" or similar button**
4. **Enter your note content** in the text field
5. **Optionally add recipients, labels, or attachments**
6. **Submit the note**

### Note Features in Web Interface

- **Rich text editing** for formatted content
- **@ mentions** to notify specific users
- **File attachments** via drag-and-drop or file picker
- **Label assignment** for categorization
- **Recipient selection** for targeted notifications

**Note:** Specific UI details may vary based on ftrack version and configuration.

## Programmatic Note Creation with Python API {#python-api}

### Prerequisites

```python
import ftrack_api

# Create session
session = ftrack_api.Session(
    server_url="https://your-company.ftrackapp.com",
    api_key="your-api-key",
    api_user="your-api-user"
)
```

### Method 1: Using Helper Method (Recommended)

```python
# Get the entity to add a note to
task = session.query('Task').first()
user = session.query('User').first()

# Create note using helper method
note = task.create_note('My new note content', author=user)

# Commit changes
session.commit()
```

### Method 2: Manual Creation

```python
# Create note manually
note = session.create('Note', {
    'content': 'My new note content',
    'author': user
})

# Attach to entity
task['notes'].append(note)

# Commit changes
session.commit()
```

**Source:** [ftrack Developer Documentation - Working with Entities](https://developer.ftrack.com/api-clients/python/working-with-entities)

## Note Features and Capabilities {#features}

### 1. Replies to Notes

```python
# Get existing note
first_note = task['notes'][0]

# Create reply using helper method
reply = first_note.create_reply('My reply to the note', author=user)

# Or create manually
reply = session.create('Note', {
    'content': 'My reply content',
    'author': user
})
first_note.replies.append(reply)

session.commit()
```

### 2. Note Labels (ftrack 4.3+)

```python
# Get label
label = session.query('NoteLabel where name is "External Note"').first()

# Create note with label
note = task.create_note(
    'Note with label',
    author=user,
    labels=[label]
)

# Or add label manually
session.create('NoteLabelLink', {
    'note_id': note['id'],
    'label_id': label['id']
})
```

### 3. Note Categories (Legacy - pre 4.3)

```python
# Get category
category = session.query('NoteCategory where name is "External Note"').first()

# Create note with category
note = task.create_note(
    'Note with category',
    author=user,
    category=category
)
```

### 4. Recipients and Notifications

```python
# Get users/groups to notify
john = session.query('User where username is "john"').one()
animation_group = session.query('Group where name is "Animation"').first()

# Create note with recipients
note = task.create_note(
    'Note with recipients',
    author=user,
    recipients=[john, animation_group]
)
```

### 5. File Attachments

```python
# Get server location
server_location = session.query('Location where name is "ftrack.server"').one()

# Create component for file
component = session.create_component(
    '/path/to/file',
    data={'name': 'My file'},
    location=server_location
)

# Attach to note
session.create('NoteComponent', {
    'component_id': component['id'],
    'note_id': note['id']
})

session.commit()
```

**Source:** [ftrack Developer Documentation - Using Notes](https://developer.ftrack.com/api-clients/examples/note/)

## Code Examples {#examples}

### Complete Example: Creating a Note with All Features

```python
import ftrack_api

# Initialize session
session = ftrack_api.Session()

# Get entities
task = session.query('Task').first()
user = session.query('User').first()
john = session.query('User where username is "john"').first()
label = session.query('NoteLabel where name is "Important"').first()

# Create comprehensive note
note = task.create_note(
    content='This is a comprehensive note with all features.',
    author=user,
    recipients=[john],
    labels=[label]
)

# Add file attachment
server_location = session.query('Location where name is "ftrack.server"').one()
component = session.create_component(
    '/path/to/attachment.pdf',
    data={'name': 'Reference Document'},
    location=server_location
)

session.create('NoteComponent', {
    'component_id': component['id'],
    'note_id': note['id']
})

# Commit all changes
session.commit()

print(f"Note created with ID: {note['id']}")
```

### Retrieving Notes from Entities

```python
# Method 1: Using relationship property
notes = task['notes']

# Method 2: Direct query
notes = session.query(f'Note where parent_id is "{task["id"]}"')

# Access note properties
for note in notes:
    print(f"Note: {note['content']}")
    print(f"Author: {note['author']['username']}")
    print(f"Created: {note['date']}")

    # Get attachments
    for note_component in note['note_components']:
        download_url = server_location.get_url(note_component['component'])
        print(f"Attachment: {download_url}")
```

## Best Practices {#best-practices}

### 1. Performance Considerations

- **Use projections** when querying notes to limit data transfer:

  ```python
  notes = session.query('select content, author.username from Note where parent_id is "{}"'.format(task['id']))
  ```

- **Populate entities** efficiently when accessing multiple attributes:
  ```python
  session.populate(notes, 'content, author, date')
  ```

### 2. Error Handling

```python
try:
    note = task.create_note('My note', author=user)
    session.commit()
except Exception as e:
    print(f"Failed to create note: {e}")
    session.rollback()
```

### 3. Note Organization

- **Use labels consistently** for categorization
- **Set appropriate recipients** to avoid notification spam
- **Include meaningful content** that provides context
- **Use replies** to maintain conversation threads

### 4. File Attachments

- **Check file size limits** before uploading
- **Use meaningful component names**
- **Verify file upload success** before creating NoteComponent links

## Resources and Documentation Links {#resources}

### Official ftrack Documentation

1. **[ftrack Developer Hub](https://developer.ftrack.com/)**

   - Main entry point for all development resources

2. **[Using Notes - Python API Examples](https://developer.ftrack.com/api-clients/examples/note/)**

   - Comprehensive guide to working with notes programmatically

3. **[Working with Entities](https://developer.ftrack.com/api-clients/python/working-with-entities)**

   - Details on entity manipulation including note creation helper methods

4. **[Python API Client Documentation](https://developer.ftrack.com/api-clients/python/)**

   - Complete Python API reference and guides

5. **[API Operations Reference](https://developer.ftrack.com/api/operations/)**
   - Low-level API operations documentation

### Community Resources

6. **[ftrack Community Forum](https://forum.ftrack.com/)**

   - Community discussions and support
   - API section: https://forum.ftrack.com/forum/21-api/

7. **[ftrack Python API GitHub Repository](https://github.com/ftrackhq/ftrack-python-api)**

   - Source code and examples

8. **[API Reference Documentation](https://ftrack-python-api.readthedocs.io/en/stable/api_reference/)**
   - Detailed API reference (note: some sections deprecated, refer to new docs)

### Additional Resources

9. **[ftrack Help Center](https://help.ftrack.com/en/)**

   - User-facing documentation and guides

10. **[ftrack Blog](https://www.ftrack.com/en/blog)**
    - Latest updates and feature announcements

## Technical Notes

### Entity Types Supporting Notes

Notes can be added to most entity types in ftrack, including:

- Projects
- Sequences
- Shots
- Tasks
- Assets
- Versions
- Reviews
- And more...

### API Limitations

Current limitations in the ftrack API (as of documentation review):

- Cannot use `parent` property when querying notes
- Cannot access `parent` property directly on note entities
- Must use `parent_id` for queries instead

### Version Compatibility

- **Note Labels**: Available in ftrack server version 4.3+
- **Note Categories**: Legacy feature for pre-4.3 versions
- **File Attachments**: Available in all modern versions
- **Recipients**: Available in all modern versions

---

**Report Generated**: August 29, 2025  
**Data Sources**: Official ftrack documentation, developer guides, and community resources  
**Methodology**: Comprehensive web search and documentation analysis using ftrack web search MCP server
