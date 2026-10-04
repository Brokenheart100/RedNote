from __future__ import annotations

from datetime import datetime
from pathlib import Path
import shutil
import subprocess
import sys
import time


ROOT = Path(__file__).resolve().parent.parent

APPHOST_PROJECT = (
    ROOT
    / "RedNote.AppHost"
    / "RedNote.AppHost.csproj"
)

OUTPUT_PATH = (
    ROOT
    / "RedNote.AppHost"
    / "aspire-output"
)

ASPIRE_EXECUTABLE = shutil.which("aspire")
DOTNET_EXECUTABLE = shutil.which("dotnet")
DOCKER_EXECUTABLE = shutil.which("docker")


def print_header(
    title: str,
) -> None:
    print()
    print("=" * 80)
    print(f"🚀 {title}")
    print("=" * 80)


def print_info(
    label: str,
    value: object,
) -> None:
    print(f"ℹ️  {label}: {value}")


def format_command(
    args: list[str],
) -> str:
    return " ".join(
        f'"{arg}"'
        if " " in arg
        else arg
        for arg in args
    )


def run_capture(
    args: list[str],
) -> tuple[int, str]:
    try:
        result = subprocess.run(
            args,
            cwd=ROOT,
            text=True,
            capture_output=True,
            check=False,
            encoding="utf-8",
            errors="replace",
        )
    except OSError as exc:
        return 1, str(exc)

    stdout = result.stdout or ""
    stderr = result.stderr or ""

    output = (
        stdout
        + stderr
    ).strip()

    return result.returncode, output


def get_version(
    command: list[str],
) -> str:
    return_code, output = run_capture(
        command
    )

    if return_code != 0:
        return "不可用"

    if not output:
        return "未知"

    return output.splitlines()[0].strip()


def validate_environment() -> bool:
    print_header(
        "环境检查"
    )

    print_info(
        "Solution Root",
        ROOT,
    )

    print_info(
        "AppHost",
        APPHOST_PROJECT,
    )

    print_info(
        "Publish Output",
        OUTPUT_PATH,
    )

    print()

    if not APPHOST_PROJECT.exists():
        print(
            f"❌ AppHost 项目不存在：{APPHOST_PROJECT}"
        )
        return False

    if ASPIRE_EXECUTABLE is None:
        print(
            "❌ 未找到 Aspire CLI。"
        )
        print(
            "💡 请先在当前终端执行："
        )
        print(
            "   where.exe aspire"
        )
        return False

    if DOTNET_EXECUTABLE is None:
        print(
            "❌ 未找到 .NET SDK。"
        )
        return False

    print(
        f"✅ Aspire CLI: {ASPIRE_EXECUTABLE}"
    )

    print(
        "✅ Aspire Version: "
        f"{get_version([ASPIRE_EXECUTABLE, '--version'])}"
    )

    print(
        f"✅ .NET SDK: {DOTNET_EXECUTABLE}"
    )

    print(
        "✅ .NET Version: "
        f"{get_version([DOTNET_EXECUTABLE, '--version'])}"
    )

    if DOCKER_EXECUTABLE is None:
        print(
            "⚠️ Docker CLI 未找到。"
        )
    else:
        print(
            f"✅ Docker CLI: {DOCKER_EXECUTABLE}"
        )

        print(
            "✅ Docker Version: "
            f"{get_version([DOCKER_EXECUTABLE, '--version'])}"
        )

    return True


def print_command(
    args: list[str],
) -> None:
    print()
    print(
        "💻 执行命令："
    )
    print()
    print(
        f"   {format_command(args)}"
    )


def print_output_directory() -> None:
    print_header(
        "发布产物"
    )

    if not OUTPUT_PATH.exists():
        print(
            f"⚠️ 输出目录不存在：{OUTPUT_PATH}"
        )
        return

    entries = sorted(
        OUTPUT_PATH.iterdir(),
        key=lambda path: (
            not path.is_dir(),
            path.name.lower(),
        ),
    )

    if not entries:
        print(
            "⚠️ 输出目录为空。"
        )
        return

    for path in entries:
        icon = (
            "📁"
            if path.is_dir()
            else "📄"
        )

        if path.is_file():
            try:
                size = path.stat().st_size

                print(
                    f"   {icon} {path.name} "
                    f"({size:,} bytes)"
                )
            except OSError:
                print(
                    f"   {icon} {path.name}"
                )
        else:
            print(
                f"   {icon} {path.name}"
            )


def publish() -> int:
    if ASPIRE_EXECUTABLE is None:
        print(
            "❌ Aspire CLI 路径不可用。"
        )
        return 1

    print_header(
        "RedNote Aspire Publish"
    )

    started_at = datetime.now()
    started = time.perf_counter()

    print_info(
        "开始时间",
        started_at.strftime(
            "%Y-%m-%d %H:%M:%S"
        ),
    )

    command = [
        ASPIRE_EXECUTABLE,
        "publish",
        "--apphost",
        str(APPHOST_PROJECT),
        "--output-path",
        str(OUTPUT_PATH),
        "--pipeline-log-level",
        "trace",
        "--include-exception-details",
        "--log-level",
        "Debug",
    ]

    print_command(
        command
    )

    print()
    print(
        "📦 开始执行 Aspire Publish..."
    )

    print(
        "📋 以下为 Aspire 实时输出："
    )

    print()
    print("-" * 80)

    try:
        result = subprocess.run(
            command,
            cwd=ROOT,
            check=False,
        )
    except KeyboardInterrupt:
        print()
        print("-" * 80)
        print()
        print(
            "🛑 Publish 已被用户中断。"
        )
        return 130
    except OSError as exc:
        print()
        print("-" * 80)
        print()
        print(
            f"❌ 无法启动 Aspire CLI：{exc}"
        )
        print(
            f"📍 Aspire 路径：{ASPIRE_EXECUTABLE}"
        )
        return 1

    elapsed = (
        time.perf_counter()
        - started
    )

    finished_at = datetime.now()

    print()
    print("-" * 80)

    print_header(
        "Publish 执行结果"
    )

    print_info(
        "结束时间",
        finished_at.strftime(
            "%Y-%m-%d %H:%M:%S"
        ),
    )

    print_info(
        "总耗时",
        f"{elapsed:.2f} 秒",
    )

    print_info(
        "退出代码",
        result.returncode,
    )

    if result.returncode != 0:
        print()
        print(
            "❌ Aspire Publish 失败。"
        )
        print()
        print(
            "🔎 请重点检查上方 "
            "Aspire Pipeline 的失败步骤。"
        )
        print(
            "🧩 当前已启用："
        )
        print(
            "   • pipeline-log-level = trace"
        )
        print(
            "   • log-level = Debug"
        )
        print(
            "   • include-exception-details"
        )
        return result.returncode

    print()
    print(
        "✅ Aspire Publish 成功。"
    )

    print(
        f"📁 输出目录：{OUTPUT_PATH}"
    )

    print_output_directory()

    return 0


def main() -> None:
    if not validate_environment():
        sys.exit(1)

    exit_code = publish()

    sys.exit(
        exit_code
    )


if __name__ == "__main__":
    main()