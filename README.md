# WebSearch MCP Server

A Model Context Protocol (MCP) server for web search functionality, specifically designed to work with GitHub Copilot and search ftrack developer documentation. This server provides free, keyless web search using DuckDuckGo with configurable domain filtering.

How to use?

Just write something like "create a report on how we can add notes to entities on ftrack. add details and links to where you pull the data from" in copilot :)

Check ftrack_notes_report.md for a result

## Features

- **Free Web Search**: Uses DuckDuckGo's free APIs - no API keys required
- **Domain Filtering**: Configurable list of allowed domains to restrict search results
- **Ftrack-Optimized**: Pre-configured for ftrack developer documentation and related resources
- **MCP Compatible**: Works seamlessly with GitHub Copilot and other MCP clients
- **Multiple Search Tools**: General web search, domain filtering, and specialized ftrack documentation search

## Available Tools

### 1. `web_search`

General web search with optional domain filtering.
