# CLAUDE.md

Instructions for this repository are shared by all coding agents and live in `AGENTS.md`, imported below. Edit rules there, not here; add to this file only what applies to Claude Code alone.

@AGENTS.md

## Claude Code only

`.claude/settings.json` asks for confirmation before deployment commands and `git push`, and blocks reading `.env*`, `*.tfvars`, Terraform state and plan files. Don't work around these rules with other tools or shell commands such as `cat` or `Get-Content`.
