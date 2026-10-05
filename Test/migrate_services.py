from __future__ import annotations

from datetime import datetime
from pathlib import Path
import subprocess
import sys
from typing import TypedDict


ROOT = Path(__file__).resolve().parent.parent


class ServiceConfig(TypedDict):
    name: str
    project: Path
    context: str
    output: str


SERVICES: list[ServiceConfig] = [
    {
        "name": "IdentityService",
        "project": (
            ROOT
            / "RedNote.IdentityService"
            / "RedNote.IdentityService.csproj"
        ),
        "context": (
            "RedNote.IdentityService."
            "Infrastructure.Persistence."
            "IdentityServiceDbContext"
        ),
        "output": (
            "Infrastructure/"
            "Persistence/"
            "Migrations"
        ),
    },
    {
        "name": "UserService",
        "project": (
            ROOT
            / "RedNote.UserService"
            / "RedNote.UserService.csproj"
        ),
        "context": (
            "RedNote.UserService."
            "Infrastructure.Persistence."
            "UserServiceDbContext"
        ),
        "output": (
            "Infrastructure/"
            "Persistence/"
            "Migrations"
        ),
    },
    {
        "name": "ContentService",
        "project": (
            ROOT
            / "RedNote.ContentService"
            / "RedNote.ContentService.csproj"
        ),
        "context": (
            "RedNote.ContentService."
            "Infrastructure.Persistence."
            "ContentServiceDbContext"
        ),
        "output": (
            "Infrastructure/"
            "Persistence/"
            "Migrations"
        ),
    },
    {
        "name": "MediaService",
        "project": (
            ROOT
            / "RedNote.MediaService"
            / "RedNote.MediaService.csproj"
        ),
        "context": (
            "RedNote.MediaService."
            "Infrastructure.Persistence."
            "MediaServiceDbContext"
        ),
        "output": (
            "Infrastructure/"
            "Persistence/"
            "Migrations"
        ),
    },
]


def run(
    args: list[str],
    *,
    capture: bool = False,
) -> subprocess.CompletedProcess[str]:
    print()
    print(
        "💻",
        ">",
        " ".join(args),
    )

    return subprocess.run(
        args,
        cwd=ROOT,
        text=True,
        capture_output=capture,
        check=False,
        encoding="utf-8",
        errors="replace",
    )


def ef_args(
    service: ServiceConfig,
) -> list[str]:
    project = str(
        service["project"]
    )

    return [
        "--project",
        project,
        "--startup-project",
        project,
        "--context",
        service["context"],
    ]


def build_service(
    service: ServiceConfig,
) -> bool:
    name = service["name"]
    project = str(
        service["project"]
    )

    print()
    print(
        f"🔨 正在构建 {name}..."
    )

    result = run(
        [
            "dotnet",
            "build",
            project,
        ]
    )

    if result.returncode != 0:
        print()
        print(
            f"❌ {name}: Build 失败"
        )

        return False

    print()
    print(
        f"✅ {name}: Build 成功"
    )

    return True


def check_pending_model_changes(
    service: ServiceConfig,
) -> bool | None:
    name = service["name"]

    print()
    print(
        f"🔍 正在检查 {name} 模型变化..."
    )

    result = run(
        [
            "dotnet",
            "ef",
            "migrations",
            "has-pending-model-changes",
            *ef_args(service),
            "--no-build",
        ],
        capture=True,
    )

    stdout = (
        result.stdout
        or ""
    )

    stderr = (
        result.stderr
        or ""
    )

    output = (
        stdout
        + stderr
    ).strip()

    if output:
        print()
        print(output)

    if (
        "No changes have been made"
        in output
    ):
        print()
        print(
            f"✅ {name}: "
            "没有待迁移的模型变化"
        )

        return False

    if (
        "Changes have been made"
        in output
    ):
        print()
        print(
            f"🟡 {name}: "
            "检测到模型变化"
        )

        return True

    if result.returncode == 0:
        print()
        print(
            f"✅ {name}: "
            "没有待迁移的模型变化"
        )

        return False

    print()
    print(
        f"❌ {name}: "
        "无法判断模型状态"
    )

    return None


def create_migration(
    service: ServiceConfig,
) -> bool:
    name = service["name"]
    output = service["output"]

    migration_name = (
        datetime.now()
        .strftime(
            "Auto%Y%m%d%H%M%S"
        )
    )

    print()
    print(
        f"🧱 正在为 {name} "
        "创建 Migration..."
    )

    print(
        f"📦 Migration: "
        f"{migration_name}"
    )

    result = run(
        [
            "dotnet",
            "ef",
            "migrations",
            "add",
            migration_name,
            *ef_args(service),
            "--output-dir",
            output,
            "--no-build",
        ]
    )

    if result.returncode != 0:
        print()
        print(
            f"❌ {name}: "
            "创建 Migration 失败"
        )

        return False

    print()
    print(
        f"✅ {name}: "
        f"已创建 {migration_name}"
    )

    return True


def update_database(
    service: ServiceConfig,
) -> bool:
    name = service["name"]

    print()
    print(
        f"🗄️ 正在更新 {name} 数据库..."
    )

    result = run(
        [
            "dotnet",
            "ef",
            "database",
            "update",
            *ef_args(service),
            "--no-build",
        ]
    )

    if result.returncode != 0:
        print()
        print(
            f"❌ {name}: "
            "数据库更新失败"
        )

        return False

    print()
    print(
        f"✅ {name}: "
        "数据库已更新"
    )

    return True


def migrate(
    service: ServiceConfig,
) -> bool:
    name = service["name"]

    print()
    print(
        "=" * 70
    )

    print(
        f"🚀 {name}"
    )

    print(
        "=" * 70
    )

    if not service["project"].exists():
        print()
        print(
            f"❌ 项目不存在："
            f"{service['project']}"
        )

        return False

    if not build_service(
        service
    ):
        return False

    pending = (
        check_pending_model_changes(
            service
        )
    )

    if pending is None:
        return False

    if pending:
        if not create_migration(
            service
        ):
            return False
    else:
        print()
        print(
            f"✅ {name}: "
            "无需创建新的 Migration"
        )

    if not update_database(
        service
    ):
        return False

    return True


def print_summary(
    failed: list[str],
) -> None:
    print()
    print(
        "=" * 70
    )

    if failed:
        print(
            "❌ Migration 处理失败"
        )

        print()

        for name in failed:
            print(
                f"   ❌ {name}"
            )

        print()
        print(
            "=" * 70
        )

        return

    print(
        "✅ 所有服务 Migration "
        "与数据库更新完成"
    )

    print()

    print(
        "🚀 现在可以直接启动 Aspire。"
    )

    print(
        "=" * 70
    )


def main() -> None:
    print()

    print(
        "=" * 70
    )

    print(
        "🚀 RedNote EF Core Migration"
    )

    print(
        "=" * 70
    )

    print()

    print(
        f"📁 Solution Root: {ROOT}"
    )

    print()
    print(
        "⚠️ 请确保 Aspire 中的 PostgreSQL "
        "已经启动并监听 localhost:6543。"
    )

    failed: list[str] = []

    for service in SERVICES:
        success = migrate(
            service
        )

        if not success:
            failed.append(
                service["name"]
            )

    print_summary(
        failed
    )

    if failed:
        sys.exit(1)


if __name__ == "__main__":
    main()
