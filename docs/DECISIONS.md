# FlowEngine Architecture Decision Records

**Purpose**: Lightweight decision log for key technical and strategic decisions  
**Format**: Chronological (newest first) with clear rationale and consequences

---

## 2025-01-12: Documentation Strategy - Archive Historical Content

**Status**: Decided  
**Context**: Documentation analysis revealed conflicting information between historical aspirations and current reality, creating credibility issues for AI Architect and development planning.

**Decision**: 
- Archive all Phase 1/2/3 historical documents with clear "historical reference only" warnings
- Create evidence-based current documentation (capabilities.md, limitations.md)
- Establish single source of truth in git repository
- AI Architect to reference only current documentation for capability questions

**Consequences**:
- ✅ Eliminates confusion between planned vs implemented features
- ✅ Provides honest foundation for development planning
- ✅ Improves AI Architect accuracy for current state questions
- ⚠️ Requires discipline to keep current docs synchronized with code

---

## 2025-01-12: Strategic Focus - Production Hardening Over Feature Expansion

**Status**: Decided  
**Context**: Feature Matrix analysis revealed FlowEngine has solid technical foundation but significant production readiness gaps. Choice between expanding features vs hardening existing capabilities.

**Decision**: 
- Focus on making existing CSV and JavaScript transform capabilities production-ready
- No new data sources until current capabilities are bulletproof
- Prioritize simplification and reliability over feature breadth
- Target enterprise production readiness over rapid feature delivery

**Consequences**:
- ✅ Builds genuine value users can trust and deploy
- ✅ Maintains positioning flexibility for future decisions
- ✅ Addresses real user pain points (reliability, error handling)
- ⚠️ May disappoint stakeholders expecting rapid feature expansion
- ⚠️ Requires 18+ month timeline for full enterprise production readiness

---

## 2025-01-12: Performance Claims - Evidence-Based Documentation

**Status**: Decided  
**Context**: Documentation contained inflated performance claims (441K rows/sec, sub-nanosecond access) that didn't match measured reality (4K-7K rows/sec, 25ns access).

**Decision**: 
- Replace all performance claims with measured results from actual pipeline execution
- Document performance characteristics honestly with test scenarios
- Include V8 JavaScript engine overhead in realistic throughput expectations
- Base all technical claims on Feature Matrix evidence

**Consequences**:
- ✅ Builds credibility through honest performance reporting
- ✅ Sets realistic user expectations
- ✅ Provides accurate baseline for performance improvement tracking
- ⚠️ May reduce perceived competitive advantage vs marketing claims
- ✅ Enables accurate capacity planning for production deployments

---

## 2025-01-12: Development Workflow - Sprint-Lite Methodology

**Status**: Decided  
**Context**: Need structured development approach for production hardening while avoiding heavyweight process overhead.

**Decision**: 
- Implement sprint-lite approach with hardening (2 weeks), simplification (1 week), and polish (1 week) sprint types
- Maintain active sprint documentation in git repository
- Update capabilities/limitations documentation with each sprint completion
- Use evidence-based progress tracking vs aspirational planning

**Consequences**:
- ✅ Provides structure without heavyweight process
- ✅ Maintains focus on production readiness goals
- ✅ Creates transparent progress tracking
- ✅ Enables iterative improvement with clear milestones
- ⚠️ Requires discipline to maintain documentation current

---

## 2025-07-12: Plugin Loading Encapsulation - Service Merging Approach

**Status**: Decided  
**Context**: Encountered type name conflicts between existing IPluginDiscoveryService and new bootstrap services during mini-sprint implementation.

**Decision**: 
- Extend existing IPluginDiscoveryService with bootstrap methods rather than creating competing services
- Merge functionality to eliminate duplicate interfaces and type conflicts
- Implement build-specific assembly filtering using compiler directives to avoid assembly loading warnings

**Consequences**:
- ✅ Maintains clean architecture without namespace pollution
- ✅ Eliminates type conflicts and registration issues
- ✅ Reduces complexity for future developers
- ✅ Successfully resolved assembly loading warnings
- ✅ Enables unified plugin discovery for both manifest and bootstrap scenarios

---

## 2025-07-11: AppendMode Configuration - Critical File Handling Fix

**Status**: Decided  
**Context**: DelimitedSink was missing AppendMode configuration mapping, causing file truncation with each chunk in multi-chunk processing scenarios.

**Decision**: 
- Add missing AppendMode configuration property mapping to DelimitedSink
- Default AppendMode to true for multi-chunk processing scenarios
- Distinguish between job-level file handling (OverwriteExisting) and chunk-level behavior (AppendMode)

**Consequences**:
- ✅ CRITICAL FIX: Enables proper large dataset processing without data loss
- ✅ Resolves file truncation issues in production-like scenarios
- ⚠️ Reveals need for broader file handling semantics review in future sprint
- ✅ Validates multi-chunk processing architecture for large datasets

---

## Template for Future Decisions

```markdown
## YYYY-MM-DD: [Decision Title]
**Status**: Decided|Superseded|Under Review
**Context**: Why this decision came up and what factors were considered
**Decision**: What we decided to do and key implementation details
**Consequences**: 
- ✅ Positive outcomes and benefits
- ⚠️ Trade-offs and risks
- ❌ Negative impacts (if any)
```

---

**Maintenance Note**: Add new decisions at the top. Mark superseded decisions but keep them for historical context. Review quarterly for decisions that need updates or clarification.