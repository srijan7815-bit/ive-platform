package com.srijan.ive

import com.srijan.ive.agent.Sandbox
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import java.nio.file.Files

class SandboxTest {
    private val root = Files.createTempDirectory("ive").toFile()
    private val sandbox = Sandbox(root)

    @Test fun resolvesRelativePathsInsideRoot() {
        assertEquals(sandbox.root.resolve("notes/a.txt").path, sandbox.resolve("notes/a.txt").path)
        assertTrue(sandbox.resolve("/leading/slash.txt").path.startsWith(sandbox.root.path))
    }

    @Test fun rejectsTraversalOutsideRoot() {
        listOf("../escape.txt", "a/../../escape.txt", "..").forEach {
            try {
                sandbox.resolve(it)
                fail("expected $it to be rejected")
            } catch (e: IllegalArgumentException) {
            }
        }
    }
}
