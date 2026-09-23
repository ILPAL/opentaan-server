package io.opentaan.admin;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.methods;
import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.noClasses;

import com.tngtech.archunit.core.importer.ImportOption;
import com.tngtech.archunit.junit.AnalyzeClasses;
import com.tngtech.archunit.junit.ArchTest;
import com.tngtech.archunit.lang.ArchRule;
import jakarta.persistence.Entity;
import org.springframework.data.repository.Repository;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.stereotype.Service;
import org.springframework.web.bind.annotation.RestController;

/** Design rules that would otherwise live only in code review. */
@AnalyzeClasses(packages = "io.opentaan.admin", importOptions = ImportOption.DoNotIncludeTests.class)
class ArchitectureTest {

    @ArchTest
    static final ArchRule every_endpoint_checks_a_permission = methods()
            .that().areDeclaredInClassesThat().areAnnotatedWith(RestController.class)
            .and().arePublic()
            .should().beAnnotatedWith(PreAuthorize.class)
            .because("no endpoint may ship without a permission check");

    @ArchTest
    static final ArchRule controllers_do_not_touch_repositories = noClasses()
            .that().areAnnotatedWith(RestController.class)
            .should().dependOnClassesThat().areAssignableTo(Repository.class)
            .because("controllers go through a service");

    @ArchTest
    static final ArchRule controllers_do_not_see_database_rows = noClasses()
            .that().areAnnotatedWith(RestController.class)
            .should().dependOnClassesThat().areAnnotatedWith(Entity.class)
            .because("the API model and the database mapping change independently");

    @ArchTest
    static final ArchRule services_know_nothing_about_http = noClasses()
            .that().areAnnotatedWith(Service.class)
            .should().dependOnClassesThat().resideInAnyPackage(
                    "org.springframework.web..", "org.springframework.http..", "..admin.api..")
            .because("services are called by HTTP today and by other entry points later");

    @ArchTest
    static final ArchRule generated_api_depends_on_nothing_of_ours = noClasses()
            .that().resideInAPackage("..admin.api..")
            .should().dependOnClassesThat().resideInAnyPackage(
                    "..admin.accounts..", "..admin.security..", "..admin.web..")
            .because("the generated contract code must not know how it is implemented");
}
