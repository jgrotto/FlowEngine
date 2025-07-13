# FlowEngine Unified Documentation Strategy

**Date**: January 2025  
**Purpose**: Define single-source documentation approach for FlowEngine development  
**Principle**: All documentation lives in git repository only

## Overview

This document defines FlowEngine's documentation strategy, emphasizing simplicity, single-source-of-truth, and alignment with our sprint-lite development workflow.

## Core Principles

1. **Single Source**: All documentation lives in the git repository
2. **Version Controlled**: Documentation versioned alongside code
3. **Minimal but Sufficient**: Just enough docs to be effective
4. **Living Documents**: Updated continuously as part of development
5. **No Duplication**: Information exists in one place only

## Repository Documentation Structure

```
FlowEngine/
├── README.md                    # 1 page - Project overview & quick start
├── CONTRIBUTING.md              # 1 page - Contribution guidelines
├── docs/
│   ├── WORKFLOW.md             # 2-3 pages - Development process
│   ├── ROADMAP.md              # 1-2 pages - Priorities & direction
│   ├── DECISIONS.md            # Ongoing - Lightweight decision log
│   ├── current/
│   │   ├── capabilities.md     # What FlowEngine does today
│   │   ├── limitations.md      # Honest boundaries
│   │   └── configuration.md    # YAML reference
│   └── sprints/
│       ├── active/
│       │   └── README.md       # Current sprint work
│       └── completed/
│           └── YYYY-MM-name.md # Archived sprints
```

## Document Purposes

### Root Level Documents

**README.md**
- First thing visitors see
- What is FlowEngine
- Quick start example
- Links to detailed docs

**CONTRIBUTING.md**
- How to set up development environment
- Code standards
- PR process
- Where to get help

### Core Workflow Documents

**WORKFLOW.md**
- Sprint-lite methodology
- Daily development rhythm
- Definition of done
- Team practices

**ROADMAP.md**
- Current focus area
- Prioritized backlog
- Completed milestones
- Non-committal future ideas

**DECISIONS.md**
- Lightweight ADR format
- Key technical decisions
- Rationale and trade-offs
- Chronological (newest first)

### Current State Documentation

**capabilities.md**
- Factual list of working features
- Performance characteristics
- Example use cases
- Based on Feature Matrix findings

**limitations.md**
- What FlowEngine doesn't do
- Known issues
- Performance boundaries
- Helps set expectations

**configuration.md**
- Complete YAML reference
- Every parameter explained
- Working examples
- Common patterns

### Sprint Documentation

**active/README.md**
- Single active sprint at a time
- Goals, tasks, progress
- Daily updates
- Test results

**completed/YYYY-MM-name.md**
- Archived when sprint ends
- Achievements summary
- Lessons learned
- Links to commits/PRs

## Documentation Workflow

### During Development

1. **Start of Sprint**
   - Create new sprint doc from template
   - Update ROADMAP.md
   - Link from WORKFLOW.md

2. **Daily Development**
   - Update sprint progress inline
   - Add decisions to DECISIONS.md
   - Update configuration.md if needed

3. **Code Changes**
   - Update relevant docs in same PR
   - Keep examples working
   - Document breaking changes

4. **End of Sprint**
   - Archive sprint doc
   - Update capabilities/limitations
   - Update ROADMAP.md
   - Plan next sprint

### Documentation Standards

**Writing Style**
- Clear and concise
- Present tense
- Code examples that work
- Honest about limitations

**Formatting**
- Markdown only
- Consistent headers
- Code blocks with language tags
- Tables for structured data

**Maintenance**
- Remove outdated content
- Fix broken links
- Update examples
- Prune completed items

## Migration Plan

### Week 1: Structure Setup
- [ ] Create docs/ structure
- [ ] Write WORKFLOW.md
- [ ] Create initial ROADMAP.md
- [ ] Start DECISIONS.md

### Week 2: Content Migration
- [ ] Convert Feature Matrix to capabilities.md
- [ ] Document current limitations
- [ ] Create configuration reference
- [ ] Archive old docs

### Ongoing: Single Source
- All new docs in repo only
- No external documentation
- Regular pruning of old content
- Keep docs synchronized with code

## Anti-Patterns to Avoid

❌ **Don't**
- Create documents "for later"
- Duplicate information
- Write lengthy plans
- Let docs drift from code
- Create complex templates
- Maintain external wikis

✅ **Do**
- Write minimal sufficient docs
- Update docs with code
- Delete outdated content
- Link instead of duplicate
- Keep docs scannable
- Focus on daily utility

## Success Metrics

- Docs answer dev questions without searching
- New contributors productive in < 1 hour
- No stale documentation > 1 sprint old
- All examples run successfully
- Single place to find any information

## Templates

### Sprint Document Template
```markdown
# Sprint: [Name]
**Dates**: [Start - End]
**Goal**: [One sentence goal]

## Tasks
- [ ] Task 1
- [ ] Task 2

## Progress
**Day 1**: What happened
**Day 2**: What happened

## Decisions
- Key decision and rationale

## Results
- What was achieved
```

### Decision Entry Template
```markdown
## YYYY-MM-DD: [Decision Title]
**Status**: Decided|Superseded
**Context**: Why this came up
**Decision**: What we decided
**Consequences**: What this means
```

## Conclusion

This unified documentation strategy provides just enough structure to be effective without creating documentation burden. By keeping everything in git, we ensure documentation evolves with the code and remains the single source of truth for FlowEngine development.