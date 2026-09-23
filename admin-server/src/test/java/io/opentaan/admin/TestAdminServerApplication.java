package io.opentaan.admin;

import org.springframework.boot.SpringApplication;

/** Runs the admin server locally against a throwaway PostgreSQL container. Needs Docker. */
public class TestAdminServerApplication {

    public static void main(String[] args) {
        SpringApplication.from(AdminServerApplication::main)
                .with(TestcontainersConfiguration.class)
                .run(args);
    }
}
