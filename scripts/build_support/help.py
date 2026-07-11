import argparse


class MultilineHelpFormatter(argparse.RawTextHelpFormatter):
    pass


def format_choice_help(summary: str, choices: list[tuple[str, str]]) -> str:
    width = max(len(name) for name, _ in choices)
    lines = [summary]
    lines.extend(f"  {name:<{width}}  {description}" for name, description in choices)
    return "\n".join(lines)
